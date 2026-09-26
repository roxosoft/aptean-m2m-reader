using JetSolutions.BillVendorSync.BillDotCom;
using JetSolutions.BillVendorSync.Config;
using JetSolutions.BillVendorSync.Excel;
using JetSolutions.BillVendorSync.Matching;
using JetSolutions.BillVendorSync.Reporting;

namespace JetSolutions.BillVendorSync;

/// <summary>
/// Matches M2M vendors to Bill.com and optionally applies companyName updates.
/// </summary>
public static class VendorSyncPipeline
{
    private const string PlaceholderCity = "TBD";
    private const string PlaceholderZip = "00000";

    /// <param name="autoApplyExact">When true, apply exact matches without Y/N.</param>
    /// <param name="autoApplyRelaxed">When true, apply relaxed matches without Y/N.</param>
    public static async Task RunAsync(
        IReadOnlyList<M2MVendor> m2mVendors,
        BillDotComSettings billSettings,
        bool autoApplyExact = false,
        bool autoApplyRelaxed = false,
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
            var outcomes = new Dictionary<MatchResult, UpdateOutcome>();

            if (exactUpdates.Count == 0 && relaxedUpdates.Count == 0)
            {
                Console.WriteLine();
                Console.WriteLine("No vendors require an update.");
            }

            if (exactUpdates.Count > 0)
            {
                if (autoApplyExact || Confirm($"Apply {exactUpdates.Count} EXACT update(s)? (Y/N): "))
                {
                    if (autoApplyExact)
                    {
                        Console.WriteLine($"Auto-applying {exactUpdates.Count} EXACT update(s)...");
                    }

                    await ApplyAllAsync(client, exactUpdates, outcomes, "exact", ct);
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

                    await ApplyAllAsync(client, relaxedUpdates, outcomes, "relaxed", ct);
                }
                else
                {
                    Console.WriteLine("Relaxed updates skipped by user.");
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

    private static async Task ApplyAllAsync(
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

    private static async Task<UpdateOutcome> ApplyUpdateAsync(BillClient client, MatchResult result, CancellationToken ct)
    {
        var outcome = new UpdateOutcome { Result = result };
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
