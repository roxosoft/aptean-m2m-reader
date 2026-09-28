using JetSolutions.BillVendorSync.BillDotCom;
using JetSolutions.BillVendorSync.Config;
using JetSolutions.BillVendorSync.Excel;
using JetSolutions.BillVendorSync.Matching;
using JetSolutions.BillVendorSync.Reporting;

namespace JetSolutions.BillVendorSync;

/// <summary>
/// Matches M2M vendors to Bill.com, updates companyName on matches, and creates unmatched M2M vendors.
/// </summary>
public static class VendorSyncPipeline
{
    private const string PlaceholderCity = "TBD";
    private const string PlaceholderZip = "00000";
    private const string AutoImportAccountNumber = "M2M-AUTOIMPORT";

    /// <param name="autoApplyExact">When true, apply exact matches without Y/N.</param>
    /// <param name="autoApplyRelaxed">When true, apply relaxed matches without Y/N.</param>
    /// <param name="autoCreate">When true, create unmatched M2M vendors without Y/N.</param>
    public static async Task RunAsync(
        IReadOnlyList<M2MVendor> m2mVendors,
        BillDotComSettings billSettings,
        bool autoApplyExact = false,
        bool autoApplyRelaxed = false,
        bool autoCreate = false,
        string reportsDirectory = "reports",
        CancellationToken ct = default)
    {
        using var client = new BillClient(billSettings);

        Console.WriteLine("Signing in to Bill.com...");
        await client.LoginAsync(ct);
        Console.WriteLine("  Login successful.");

        try
        {
            Console.WriteLine("Fetching all Bill.com vendors...");
            var billVendors = await client.GetAllVendorsAsync(ct);
            Console.WriteLine($"  Fetched {billVendors.Count} Bill.com vendor(s).");

            var matcher = new VendorMatcher();
            var results = matcher.Match(m2mVendors, billVendors);

            ReportWriter.PrintPreview(results);

            var exactUpdates = results.Where(r => r.WillUpdate && r.MatchType == VendorMatchType.Exact).ToList();
            var relaxedUpdates = results.Where(r => r.WillUpdate && r.MatchType == VendorMatchType.Relaxed).ToList();
            var toCreate = results.Where(r => r.Status == MatchStatus.Unmatched && r.M2M is not null).ToList();
            var outcomes = new Dictionary<MatchResult, UpdateOutcome>();

            if (exactUpdates.Count == 0 && relaxedUpdates.Count == 0 && toCreate.Count == 0)
            {
                Console.WriteLine();
                Console.WriteLine("No vendors require an update or create.");
            }

            if (exactUpdates.Count > 0)
            {
                if (autoApplyExact || Confirm($"Apply {exactUpdates.Count} EXACT update(s)? (Y/N): "))
                {
                    if (autoApplyExact)
                    {
                        Console.WriteLine($"Auto-applying {exactUpdates.Count} EXACT update(s)...");
                    }

                    await ApplyAllUpdatesAsync(client, exactUpdates, outcomes, "exact", ct);
                }
                else
                {
                    Console.WriteLine("Exact updates skipped by user.");
                }
            }

            if (relaxedUpdates.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine("The following RELAXED (fuzzy) matches are lower-confidence. Review them above.");
                if (autoApplyRelaxed || Confirm($"Apply {relaxedUpdates.Count} RELAXED update(s)? (Y/N): "))
                {
                    if (autoApplyRelaxed)
                    {
                        Console.WriteLine($"Auto-applying {relaxedUpdates.Count} RELAXED update(s)...");
                    }

                    await ApplyAllUpdatesAsync(client, relaxedUpdates, outcomes, "relaxed", ct);
                }
                else
                {
                    Console.WriteLine("Relaxed updates skipped by user.");
                }
            }

            if (toCreate.Count > 0)
            {
                Console.WriteLine();
                if (autoCreate || Confirm($"Create {toCreate.Count} unmatched vendor(s) in Bill.com? (Y/N): "))
                {
                    if (autoCreate)
                    {
                        Console.WriteLine($"Auto-creating {toCreate.Count} vendor(s)...");
                    }

                    await CreateAllAsync(client, toCreate, outcomes, ct);
                }
                else
                {
                    Console.WriteLine("Creates skipped by user.");
                }
            }

            ReportWriter.PrintFinalSummary(outcomes.Values.ToList(), results);

            var reportPath = ReportWriter.WriteCsv(results, outcomes, reportsDirectory);
            Console.WriteLine();
            Console.WriteLine($"Full report written to: {reportPath}");
        }
        finally
        {
            await client.LogoutAsync(ct);
        }
    }

    private static async Task ApplyAllUpdatesAsync(
        BillClient client,
        IReadOnlyList<MatchResult> updates,
        Dictionary<MatchResult, UpdateOutcome> outcomes,
        string label,
        CancellationToken ct)
    {
        Console.WriteLine();
        Console.WriteLine($"Applying {updates.Count} {label} update(s)...");

        foreach (var result in updates)
        {
            outcomes[result] = await ApplyUpdateAsync(client, result, ct);
        }
    }

    private static async Task CreateAllAsync(
        BillClient client,
        IReadOnlyList<MatchResult> creates,
        Dictionary<MatchResult, UpdateOutcome> outcomes,
        CancellationToken ct)
    {
        Console.WriteLine();
        Console.WriteLine($"Creating {creates.Count} vendor(s)...");

        foreach (var result in creates)
        {
            outcomes[result] = await ApplyCreateAsync(client, result, ct);
        }
    }

    private static async Task<UpdateOutcome> ApplyUpdateAsync(BillClient client, MatchResult result, CancellationToken ct)
    {
        var outcome = new UpdateOutcome { Result = result, Action = SyncAction.Update };
        var vendor = result.BillVendor!;
        var m2mId = result.M2M!.M2MVendorId;

        try
        {
            await SendWithTransientRetryAsync(
                () => client.UpdateVendorCompanyNameAsync(vendor.Id, m2mId, ct: ct), ct);
            outcome.Applied = true;
            Console.WriteLine($"  [OK]   {vendor.Name} ({vendor.Id}) -> companyName '{m2mId}'");
            return outcome;
        }
        catch (BillApiException ex) when (IsIncompleteAddress(ex))
        {
            var existing = vendor.Address;
            bool needCity = string.IsNullOrWhiteSpace(existing?.City);
            bool needZip = string.IsNullOrWhiteSpace(existing?.ZipOrPostalCode);

            var m2mCity = result.M2M!.City;
            var m2mZip = result.M2M!.ZipCode;

            string cityToUse = !string.IsNullOrWhiteSpace(m2mCity) ? m2mCity! : PlaceholderCity;
            string zipToUse = !string.IsNullOrWhiteSpace(m2mZip) ? m2mZip! : PlaceholderZip;

            bool placeholderCity = needCity && string.IsNullOrWhiteSpace(m2mCity);
            bool placeholderZip = needZip && string.IsNullOrWhiteSpace(m2mZip);

            try
            {
                var address = BuildAddressFill(vendor, cityToUse, zipToUse);
                await SendWithTransientRetryAsync(
                    () => client.UpdateVendorCompanyNameAsync(vendor.Id, m2mId, address, ct), ct);
                outcome.Applied = true;

                if (placeholderCity || placeholderZip)
                {
                    var parts = new List<string>();
                    if (placeholderCity) parts.Add($"city='{PlaceholderCity}'");
                    if (placeholderZip) parts.Add($"zip='{PlaceholderZip}'");
                    outcome.Warning = $"Saved using placeholder address ({string.Join(", ", parts)}) because it was missing in both Bill.com and the M2M vendor.";
                    Console.WriteLine($"  [OK!]  {vendor.Name} ({vendor.Id}) -> companyName '{m2mId}' (placeholder {string.Join("/", parts)} to force save)");
                }
                else
                {
                    Console.WriteLine($"  [OK*]  {vendor.Name} ({vendor.Id}) -> companyName '{m2mId}' (filled city/zip from M2M)");
                }

                return outcome;
            }
            catch (Exception ex2)
            {
                outcome.Applied = false;
                outcome.Error = $"Address-fill retry failed: {ex2.Message}";
                Console.WriteLine($"  [FAIL] {vendor.Name} ({vendor.Id}): {outcome.Error}");
                return outcome;
            }
        }
        catch (Exception ex)
        {
            outcome.Applied = false;
            outcome.Error = ex.Message;
            Console.WriteLine($"  [FAIL] {vendor.Name} ({vendor.Id}): {ex.Message}");
            return outcome;
        }
    }

    private static async Task<UpdateOutcome> ApplyCreateAsync(BillClient client, MatchResult result, CancellationToken ct)
    {
        var m2m = result.M2M!;
        var outcome = new UpdateOutcome { Result = result, Action = SyncAction.Create };

        bool placeholderCity = string.IsNullOrWhiteSpace(m2m.City);
        bool placeholderZip = string.IsNullOrWhiteSpace(m2m.ZipCode);

        var request = BuildCreateRequest(m2m, out var warning);

        try
        {
            var created = await SendWithTransientRetryAsync(
                () => client.CreateVendorAsync(request, ct), ct);
            outcome.Applied = true;
            outcome.CreatedBillVendorId = created.Id;
            outcome.Warning = warning;
            if (!string.IsNullOrWhiteSpace(warning))
            {
                Console.WriteLine($"  [NEW!] {m2m.VendorName} -> {created.Id} (companyName '{m2m.M2MVendorId}') {warning}");
            }
            else
            {
                Console.WriteLine($"  [NEW]  {m2m.VendorName} -> {created.Id} (companyName '{m2m.M2MVendorId}')");
            }

            return outcome;
        }
        catch (Exception ex)
        {
            outcome.Applied = false;
            outcome.Error = ex.Message;
            Console.WriteLine($"  [FAIL] create {m2m.VendorName}: {ex.Message}");
            return outcome;
        }
    }

    private static VendorCreateRequest BuildCreateRequest(M2MVendor m2m, out string? warning)
    {
        var warnings = new List<string>();
        string line1 = !string.IsNullOrWhiteSpace(m2m.StreetAddress) ? m2m.StreetAddress! : m2m.VendorName;
        if (string.IsNullOrWhiteSpace(m2m.StreetAddress))
        {
            warnings.Add("line1 from company name");
        }

        string city = !string.IsNullOrWhiteSpace(m2m.City) ? m2m.City! : PlaceholderCity;
        string zip = !string.IsNullOrWhiteSpace(m2m.ZipCode) ? m2m.ZipCode! : PlaceholderZip;
        if (string.IsNullOrWhiteSpace(m2m.City))
        {
            warnings.Add($"city='{PlaceholderCity}'");
        }

        if (string.IsNullOrWhiteSpace(m2m.ZipCode))
        {
            warnings.Add($"zip='{PlaceholderZip}'");
        }

        string country = !string.IsNullOrWhiteSpace(m2m.Country) ? m2m.Country! : "US";

        warning = warnings.Count == 0
            ? null
            : "Created with " + string.Join(", ", warnings);

        return new VendorCreateRequest
        {
            Name = m2m.VendorName,
            AccountNumber = AutoImportAccountNumber,
            Email = NullIfEmpty(m2m.Email),
            Phone = NullIfEmpty(m2m.Phone),
            AdditionalInfo = new AdditionalInfoDto { CompanyName = m2m.M2MVendorId },
            Address = new AddressDto
            {
                Line1 = line1,
                City = city,
                StateOrProvince = NullIfEmpty(m2m.State),
                ZipOrPostalCode = zip,
                Country = country,
            },
        };
    }

    private static string? NullIfEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static AddressDto BuildAddressFill(VendorResponseDto vendor, string city, string zip)
    {
        var existing = vendor.Address;
        return new AddressDto
        {
            Line1 = string.IsNullOrWhiteSpace(existing?.Line1) ? vendor.Name : existing!.Line1,
            City = string.IsNullOrWhiteSpace(existing?.City) ? city : existing!.City,
            StateOrProvince = existing?.StateOrProvince,
            ZipOrPostalCode = string.IsNullOrWhiteSpace(existing?.ZipOrPostalCode) ? zip : existing!.ZipOrPostalCode,
            Country = string.IsNullOrWhiteSpace(existing?.Country) ? "US" : existing!.Country,
        };
    }

    private static async Task SendWithTransientRetryAsync(Func<Task> action, CancellationToken ct)
    {
        const int maxAttempts = 3;
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                await action();
                return;
            }
            catch (BillApiException ex) when (attempt < maxAttempts && IsTransient(ex))
            {
                await Task.Delay(500 * attempt, ct);
            }
        }
    }

    private static async Task<T> SendWithTransientRetryAsync<T>(Func<Task<T>> action, CancellationToken ct)
    {
        const int maxAttempts = 3;
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return await action();
            }
            catch (BillApiException ex) when (attempt < maxAttempts && IsTransient(ex))
            {
                await Task.Delay(500 * attempt, ct);
            }
        }
    }

    private static bool IsTransient(BillApiException ex)
    {
        if (ex.StatusCode is null)
        {
            return false;
        }

        int code = (int)ex.StatusCode.Value;
        return code == 429 || code >= 500;
    }

    private static bool IsIncompleteAddress(BillApiException ex)
        => ex.StatusCode == System.Net.HttpStatusCode.BadRequest
           && ex.Message.Contains("must not be blank", StringComparison.OrdinalIgnoreCase)
           && ex.Message.Contains("address.", StringComparison.OrdinalIgnoreCase);

    private static bool Confirm(string prompt)
    {
        if (Console.IsInputRedirected)
        {
            Console.WriteLine();
            Console.WriteLine($"{prompt} (no TTY — treating as N)");
            return false;
        }

        Console.WriteLine();
        Console.Write(prompt);
        var input = Console.ReadLine()?.Trim();
        return input is not null
               && (input.Equals("y", StringComparison.OrdinalIgnoreCase)
                   || input.Equals("yes", StringComparison.OrdinalIgnoreCase));
    }
}
