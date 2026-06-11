using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using JetSolutions.BillVendorSync.Config;

namespace JetSolutions.BillVendorSync.BillDotCom;

/// <summary>
/// Thin client for the Bill.com v3 API covering the endpoints needed for this sync:
/// login, paginated vendor listing, vendor account-number update, and logout.
/// </summary>
public sealed class BillClient : IDisposable
{
    private const int PageSize = 100;

    private readonly HttpClient _http;
    private readonly BillDotComSettings _settings;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
    };

    private string? _sessionId;

    public BillClient(BillDotComSettings settings)
    {
        _settings = settings;

        var baseUrl = settings.BaseUrl.EndsWith('/') ? settings.BaseUrl : settings.BaseUrl + "/";
        _http = new HttpClient { BaseAddress = new Uri(baseUrl) };
        _http.DefaultRequestHeaders.Add("devKey", settings.DevKey);
    }

    public async Task LoginAsync(CancellationToken ct = default)
    {
        var request = new LoginRequest
        {
            DevKey = _settings.DevKey,
            Username = _settings.Username,
            Password = _settings.Password,
            OrganizationId = _settings.OrganizationId,
        };

        using var response = await _http.PostAsJsonAsync("login", request, _jsonOptions, ct);
        await EnsureSuccessAsync(response, "login", ct);

        var login = await response.Content.ReadFromJsonAsync<LoginResponse>(_jsonOptions, ct);
        if (login?.SessionId is null || login.SessionId.Length == 0)
        {
            throw new BillApiException("Login succeeded but no sessionId was returned.");
        }

        _sessionId = login.SessionId;
        _http.DefaultRequestHeaders.Remove("sessionId");
        _http.DefaultRequestHeaders.Add("sessionId", _sessionId);
    }

    public async Task<IReadOnlyList<VendorResponseDto>> GetAllVendorsAsync(CancellationToken ct = default)
    {
        EnsureLoggedIn();

        var vendors = new List<VendorResponseDto>();
        string? page = null;

        do
        {
            var url = $"vendors?max={PageSize}";
            if (!string.IsNullOrEmpty(page))
            {
                url += $"&page={Uri.EscapeDataString(page)}";
            }

            using var response = await _http.GetAsync(url, ct);
            await EnsureSuccessAsync(response, "list vendors", ct);

            var listResponse = await response.Content.ReadFromJsonAsync<VendorListResponse>(_jsonOptions, ct);
            if (listResponse?.Results is { Count: > 0 })
            {
                vendors.AddRange(listResponse.Results);
            }

            page = listResponse?.NextPage;
        }
        while (!string.IsNullOrEmpty(page));

        return vendors;
    }

    public async Task UpdateVendorCompanyNameAsync(
        string vendorId,
        string companyName,
        AddressDto? address = null,
        CancellationToken ct = default)
    {
        EnsureLoggedIn();

        var body = new VendorUpdateRequest
        {
            AdditionalInfo = new AdditionalInfoDto { CompanyName = companyName },
            Address = address,
        };
        var json = JsonSerializer.Serialize(body, _jsonOptions);

        using var request = new HttpRequestMessage(HttpMethod.Patch, $"vendors/{Uri.EscapeDataString(vendorId)}")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

        using var response = await _http.SendAsync(request, ct);
        await EnsureSuccessAsync(response, $"update vendor {vendorId}", ct);
    }

    public async Task LogoutAsync(CancellationToken ct = default)
    {
        if (_sessionId is null)
        {
            return;
        }

        try
        {
            using var response = await _http.PostAsync("logout", content: null, ct);
            // Logout failures are non-fatal; the session expires on its own.
        }
        catch
        {
            // Ignore logout errors.
        }
        finally
        {
            _sessionId = null;
        }
    }

    private void EnsureLoggedIn()
    {
        if (_sessionId is null)
        {
            throw new BillApiException("Not logged in. Call LoginAsync first.");
        }
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, string operation, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var raw = await response.Content.ReadAsStringAsync(ct);
        var message = TryFormatErrors(raw);
        throw new BillApiException(
            $"Bill.com {operation} failed ({(int)response.StatusCode} {response.StatusCode}): {message}",
            response.StatusCode);
    }

    private string TryFormatErrors(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "(no response body)";
        }

        try
        {
            var errors = JsonSerializer.Deserialize<List<BdcError>>(raw, _jsonOptions);
            if (errors is { Count: > 0 })
            {
                return string.Join("; ", errors.Select(e => $"{e.Code}: {e.Message}"));
            }
        }
        catch (JsonException)
        {
            // Fall through to returning the raw body.
        }

        return raw;
    }

    public void Dispose() => _http.Dispose();
}

public sealed class BillApiException : Exception
{
    public HttpStatusCode? StatusCode { get; }

    public BillApiException(string message, HttpStatusCode? statusCode = null) : base(message)
    {
        StatusCode = statusCode;
    }
}
