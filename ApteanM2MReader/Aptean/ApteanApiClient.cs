using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using JetSolutions.ApteanM2MReader.Config;

namespace JetSolutions.ApteanM2MReader.Aptean;

/// <summary>
/// Calls Made2Manage Web API business-object endpoints with a Bearer token.
/// </summary>
public sealed class ApteanApiClient : IDisposable
{
    /// <summary>
    /// List endpoints return a brief projection (no Zip/State/Phone).
    /// Detail GET /{VendorNumber} returns the full record.
    /// </summary>
    private const int DetailConcurrency = 8;

    private static readonly string[] VendorIdAliases =
    {
        "VendorNumber", "Vendor", "fvend", "VendorId", "VendorID", "vendor", "fVend",
    };

    private static readonly string[] CompanyAliases =
    {
        "Company", "fcompany", "VendorName", "Name", "fCompany", "company",
    };

    private static readonly string[] CityAliases =
    {
        "City", "fcity", "fCity", "city",
    };

    private static readonly string[] StateAliases =
    {
        "State", "fstate", "fState", "fstatename", "state",
    };

    private static readonly string[] ZipAliases =
    {
        "ZipCode", "Zip", "fzip", "Zip Code", "fZip", "zip", "Zip/Postal Code", "PostalCode", "fzipno",
    };

    private static readonly string[] PhoneAliases =
    {
        "Phone", "PhoneNumber", "fphoneno", "fphone", "phone",
    };

    private readonly HttpClient _http;
    private readonly ApteanSettings _settings;

    public ApteanApiClient(ApteanSettings settings)
    {
        _settings = settings;

        var baseUrl = settings.BaseUrl.TrimEnd('/') + "/";
        _http = new HttpClient { BaseAddress = new Uri(baseUrl) };
        _http.DefaultRequestHeaders.Add("CompanyID", settings.CompanyId);
        if (!string.IsNullOrWhiteSpace(settings.ClientId))
        {
            _http.DefaultRequestHeaders.Add("ClientName", settings.ClientId);
        }
    }

    public async Task<VendorFetchResult> GetAllVendorsAsync(string accessToken, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);

        var objectPath = BuildObjectPath();
        var briefRows = new List<ApteanVendor>();
        int page = 1;
        int pagesFetched = 0;
        int? totalPages = null;
        bool mappingValidated = false;

        while (true)
        {
            if (totalPages is int max && page > max)
            {
                break;
            }

            using var pageDoc = await GetJsonAsync($"{objectPath}?Page={page}", accessToken, ct, $"vendor list page {page}");
            pagesFetched++;
            totalPages ??= ReadTotalPages(pageDoc.RootElement);

            var pageItems = ExtractArray(pageDoc.RootElement).ToList();
            if (pageItems.Count == 0)
            {
                break;
            }

            if (!mappingValidated)
            {
                EnsureFieldMapping(pageItems[0]);
                mappingValidated = true;
            }

            foreach (var element in pageItems)
            {
                var mapped = MapVendor(element);
                if (mapped is not null)
                {
                    briefRows.Add(mapped);
                }
            }

            page++;
        }

        // List is brief-only; enrich Zip/State/Phone from detail GET per vendor.
        var enriched = await EnrichFromDetailsAsync(objectPath, briefRows, accessToken, ct);

        return new VendorFetchResult
        {
            Vendors = enriched.Vendors,
            PagesFetched = pagesFetched,
            DetailsFetched = enriched.DetailsFetched,
        };
    }

    private async Task<(IReadOnlyList<ApteanVendor> Vendors, int DetailsFetched)> EnrichFromDetailsAsync(
        string objectPath,
        IReadOnlyList<ApteanVendor> briefRows,
        string accessToken,
        CancellationToken ct)
    {
        if (briefRows.Count == 0)
        {
            return (briefRows, 0);
        }

        var results = new ApteanVendor[briefRows.Count];
        var detailsFetched = 0;
        using var gate = new SemaphoreSlim(DetailConcurrency);

        var tasks = briefRows.Select(async (brief, index) =>
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (string.IsNullOrWhiteSpace(brief.VendorId))
                {
                    results[index] = brief;
                    return;
                }

                var detail = await TryGetVendorDetailAsync(objectPath, brief.VendorId, accessToken, ct)
                    .ConfigureAwait(false);
                if (detail is null)
                {
                    results[index] = brief;
                    return;
                }

                Interlocked.Increment(ref detailsFetched);
                var mapped = MapVendor(detail.Value) ?? brief;
                // Prefer detail fields; fall back to brief for any still-missing values.
                results[index] = mapped with
                {
                    VendorId = FirstNonEmpty(mapped.VendorId, brief.VendorId)!,
                    Company = FirstNonEmpty(mapped.Company, brief.Company)!,
                    City = FirstNonEmpty(mapped.City, brief.City),
                    State = FirstNonEmpty(mapped.State, brief.State),
                    ZipCode = FirstNonEmpty(mapped.ZipCode, brief.ZipCode),
                    Phone = FirstNonEmpty(mapped.Phone, brief.Phone),
                };
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);
        return (results, detailsFetched);
    }

    private async Task<JsonElement?> TryGetVendorDetailAsync(
        string objectPath,
        string vendorNumber,
        string accessToken,
        CancellationToken ct)
    {
        var url = $"{objectPath}/{Uri.EscapeDataString(vendorNumber)}";
        using var request = CreateAuthorizedGet(url, accessToken);
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            // Keep the brief row rather than failing the whole export for one bad id.
            return null;
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var detail = ExtractDetailObject(doc.RootElement);
            return detail?.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private string BuildObjectPath()
    {
        var context = _settings.ContextPath.Trim().Trim('/');
        var objectName = _settings.ObjectName.Trim().Trim('/');
        if (string.IsNullOrWhiteSpace(context) || string.IsNullOrWhiteSpace(objectName))
        {
            throw new InvalidOperationException("Aptean ContextPath and ObjectName must be set.");
        }

        return $"{context}/api/{objectName}";
    }

    private async Task<JsonDocument> GetJsonAsync(
        string relativeUrl,
        string accessToken,
        CancellationToken ct,
        string contextLabel)
    {
        using var request = CreateAuthorizedGet(relativeUrl, accessToken);
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            ThrowApiError(response.StatusCode, body);
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return JsonDocument.Parse("[]");
        }

        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            throw new ApteanApiException(
                $"Aptean response was not valid JSON ({contextLabel}): {ex.Message}. Body: {Truncate(body)}",
                response.StatusCode);
        }
    }

    private static int? ReadTotalPages(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!TryGetPropertyIgnoreCase(root, "Pagination", out var pagination)
            || pagination.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var name in new[] { "NumberOfPages", "numberOfPages", "TotalPages", "totalPages", "PageCount", "pageCount" })
        {
            if (TryGetPropertyIgnoreCase(pagination, name, out var prop)
                && prop.TryGetInt32(out var pages)
                && pages > 0)
            {
                return pages;
            }
        }

        return null;
    }

    private HttpRequestMessage CreateAuthorizedGet(string relativeUrl, string accessToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, relativeUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private void ThrowApiError(HttpStatusCode statusCode, string body)
    {
        var detail = string.IsNullOrWhiteSpace(body) ? "(no response body)" : body;
        var message = $"Aptean API request failed ({(int)statusCode} {statusCode}): {detail}";

        if (statusCode == HttpStatusCode.NotFound)
        {
            message +=
                $" Check Aptean:ContextPath ('{_settings.ContextPath}') and Aptean:ObjectName ('{_settings.ObjectName}') " +
                "match this tenant (govcloud default path is {{BaseUrl}}/webapi/api/Vendor).";
        }

        throw new ApteanApiException(message, statusCode);
    }

    private static IEnumerable<JsonElement> ExtractArray(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
            {
                yield return item;
            }

            yield break;
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        foreach (var name in new[]
                 {
                     "Data", "data", "value", "Value", "results", "Results",
                     "items", "Items", "Records", "records",
                 })
        {
            if (TryGetPropertyIgnoreCase(root, name, out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in arr.EnumerateArray())
                {
                    yield return item;
                }

                yield break;
            }
        }

        if (LooksLikeVendorObject(root))
        {
            yield return root;
        }
    }

    /// <summary>Detail responses wrap a single object in <c>Data</c> (not an array).</summary>
    private static JsonElement? ExtractDetailObject(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object
            && TryGetPropertyIgnoreCase(root, "Data", out var data))
        {
            if (data.ValueKind == JsonValueKind.Object && LooksLikeVendorObject(data))
            {
                return data;
            }

            if (data.ValueKind == JsonValueKind.Array && data.GetArrayLength() > 0)
            {
                return data[0];
            }
        }

        if (LooksLikeVendorObject(root))
        {
            return root;
        }

        return null;
    }

    private static bool LooksLikeVendorObject(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var alias in VendorIdAliases.Concat(CompanyAliases))
        {
            if (TryGetPropertyIgnoreCase(root, alias, out _))
            {
                return true;
            }
        }

        return false;
    }

    private static void EnsureFieldMapping(JsonElement sample)
    {
        if (sample.ValueKind != JsonValueKind.Object)
        {
            throw new ApteanApiException("Vendor page item was not a JSON object; cannot map fields.");
        }

        bool hasId = VendorIdAliases.Any(a => TryGetPropertyIgnoreCase(sample, a, out _));
        bool hasName = CompanyAliases.Any(a => TryGetPropertyIgnoreCase(sample, a, out _));

        if (hasId && hasName)
        {
            return;
        }

        var props = sample.EnumerateObject().Select(p => p.Name).ToList();
        var propList = props.Count == 0 ? "(none)" : string.Join(", ", props);
        throw new ApteanApiException(
            "Could not map required Vendor ID / Company fields from the API response. " +
            $"Looked for ID aliases [{string.Join(", ", VendorIdAliases)}] and " +
            $"name aliases [{string.Join(", ", CompanyAliases)}]. " +
            $"Actual properties on sample record: {propList}");
    }

    private static ApteanVendor? MapVendor(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var id = GetStringByAliases(element, VendorIdAliases);
        var company = GetStringByAliases(element, CompanyAliases);

        if (string.IsNullOrWhiteSpace(id) && string.IsNullOrWhiteSpace(company))
        {
            return null;
        }

        return new ApteanVendor
        {
            VendorId = id?.Trim() ?? string.Empty,
            Company = company?.Trim() ?? string.Empty,
            City = NormalizeOptional(GetStringByAliases(element, CityAliases)),
            State = NormalizeOptional(GetStringByAliases(element, StateAliases)),
            ZipCode = NormalizeOptional(GetStringByAliases(element, ZipAliases)),
            Phone = NormalizeOptional(GetStringByAliases(element, PhoneAliases)),
        };
    }

    private static string? GetStringByAliases(JsonElement element, IEnumerable<string> aliases)
    {
        foreach (var alias in aliases)
        {
            if (TryGetPropertyIgnoreCase(element, alias, out var prop))
            {
                return prop.ValueKind switch
                {
                    JsonValueKind.String => prop.GetString(),
                    JsonValueKind.Number => prop.ToString(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    JsonValueKind.Null => null,
                    _ => prop.ToString(),
                };
            }
        }

        return null;
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string name, out JsonElement value)
    {
        if (element.TryGetProperty(name, out value))
        {
            return true;
        }

        foreach (var prop in element.EnumerateObject())
        {
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    /// <summary>Treat blank / N/A placeholder values from brief M2M lists as missing.</summary>
    private static string? NormalizeOptional(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (string.Equals(trimmed, "N/A", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return trimmed;
    }

    private static string? FirstNonEmpty(string? primary, string? fallback)
        => !string.IsNullOrWhiteSpace(primary) ? primary : NormalizeOptional(fallback);

    private static string Truncate(string text, int max = 500)
        => text.Length <= max ? text : text[..max] + "...";

    public void Dispose() => _http.Dispose();
}
