using JetSolutions.BillVendorSync.BillDotCom;
using JetSolutions.BillVendorSync.Config;
using JetSolutions.BillVendorSync.Excel;
using JetSolutions.BillVendorSync.Matching;
using JetSolutions.BillVendorSync.Reporting;
using Microsoft.Extensions.Configuration;

namespace JetSolutions.BillVendorSync;

internal static class Program
{
    // Last-resort placeholders used to satisfy Bill.com address validation when neither
    // Bill.com nor the M2M Excel row provides a city/zip for an otherwise-matched vendor.
    private const string PlaceholderCity = "TBD";
    private const string PlaceholderZip = "00000";

    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        try
        {
            var settings = LoadSettings(args);

            string excelPath = ResolveExcelPath(args, settings.Excel.Path);
            Console.WriteLine($"Reading M2M vendors from: {excelPath}");
            var m2mVendors = M2MVendorReader.Read(excelPath, settings.Excel.SheetName);
            Console.WriteLine($"  Loaded {m2mVendors.Count} M2M vendor row(s).");

            using var client = new BillClient(settings.BillDotCom);

            Console.WriteLine("Signing in to Bill.com...");
            await client.LoginAsync();
            Console.WriteLine("  Login successful.");

            try
            {
                Console.WriteLine("Fetching all Bill.com vendors...");
                var billVendors = await client.GetAllVendorsAsync();
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

                // Phase 1: exact matches.
                if (exactUpdates.Count > 0)
                {
                    if (Confirm($"Apply {exactUpdates.Count} EXACT update(s)? (Y/N): "))
                    {
                        await ApplyAllAsync(client, exactUpdates, outcomes, "exact");
                    }
                    else
                    {
                        Console.WriteLine("Exact updates skipped by user.");
                    }
                }

                // Phase 2: relaxed (fuzzy) matches, confirmed separately.
                if (relaxedUpdates.Count > 0)
                {
                    Console.WriteLine();
                    Console.WriteLine("The following RELAXED (fuzzy) matches are lower-confidence. Review them above.");
                    if (Confirm($"Apply {relaxedUpdates.Count} RELAXED update(s)? (Y/N): "))
                    {
                        await ApplyAllAsync(client, relaxedUpdates, outcomes, "relaxed");
                    }
                    else
                    {
                        Console.WriteLine("Relaxed updates skipped by user.");
                    }
                }

                ReportWriter.PrintFinalSummary(outcomes.Values.ToList(), results);

                var reportPath = ReportWriter.WriteCsv(results, outcomes, "reports");
                Console.WriteLine();
                Console.WriteLine($"Full report written to: {reportPath}");
            }
            finally
            {
                await client.LogoutAsync();
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine($"ERROR: {ex.Message}");
            return 1;
        }
    }

    private static async Task ApplyAllAsync(
        BillClient client,
        IReadOnlyList<MatchResult> updates,
        Dictionary<MatchResult, UpdateOutcome> outcomes,
        string label)
    {
        Console.WriteLine();
        Console.WriteLine($"Applying {updates.Count} {label} update(s)...");

        foreach (var result in updates)
        {
            outcomes[result] = await ApplyUpdateAsync(client, result);
        }
    }

    private static async Task<UpdateOutcome> ApplyUpdateAsync(BillClient client, MatchResult result)
    {
        var outcome = new UpdateOutcome { Result = result };
        var vendor = result.BillVendor!;
        var m2mId = result.M2M!.M2MVendorId;

        // Attempt 1: send only companyName.
        try
        {
            await SendWithTransientRetryAsync(() => client.UpdateVendorCompanyNameAsync(vendor.Id, m2mId));
            outcome.Applied = true;
            Console.WriteLine($"  [OK]   {vendor.Name} ({vendor.Id}) -> companyName '{m2mId}'");
            return outcome;
        }
        catch (BillApiException ex) when (IsIncompleteAddress(ex))
        {
            // The vendor's stored address is incomplete and Bill.com re-validates it on update.
            // Retry once, filling the missing city/zip from the matched M2M Excel row, and as a
            // last resort using placeholders to force the save when the Excel row also lacks them.
            var existing = vendor.Address;
            bool needCity = string.IsNullOrWhiteSpace(existing?.City);
            bool needZip = string.IsNullOrWhiteSpace(existing?.ZipOrPostalCode);

            var excelCity = result.M2M!.City;
            var excelZip = result.M2M!.ZipCode;

            string cityToUse = !string.IsNullOrWhiteSpace(excelCity) ? excelCity! : PlaceholderCity;
            string zipToUse = !string.IsNullOrWhiteSpace(excelZip) ? excelZip! : PlaceholderZip;

            bool placeholderCity = needCity && string.IsNullOrWhiteSpace(excelCity);
            bool placeholderZip = needZip && string.IsNullOrWhiteSpace(excelZip);

            try
            {
                var address = BuildAddressFill(vendor, cityToUse, zipToUse);
                await SendWithTransientRetryAsync(() => client.UpdateVendorCompanyNameAsync(vendor.Id, m2mId, address));
                outcome.Applied = true;

                if (placeholderCity || placeholderZip)
                {
                    var parts = new List<string>();
                    if (placeholderCity) parts.Add($"city='{PlaceholderCity}'");
                    if (placeholderZip) parts.Add($"zip='{PlaceholderZip}'");
                    outcome.Warning = $"Saved using placeholder address ({string.Join(", ", parts)}) because it was missing in both Bill.com and the M2M Excel row.";
                    Console.WriteLine($"  [OK!]  {vendor.Name} ({vendor.Id}) -> companyName '{m2mId}' (placeholder {string.Join("/", parts)} to force save)");
                }
                else
                {
                    Console.WriteLine($"  [OK*]  {vendor.Name} ({vendor.Id}) -> companyName '{m2mId}' (filled city/zip from Excel)");
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

    /// <summary>
    /// Builds an address payload that preserves the vendor's existing fields and fills in the
    /// missing city/zip from the Excel row, so the PATCH does not clear other address data.
    /// </summary>
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

    private static async Task SendWithTransientRetryAsync(Func<Task> action)
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
                await Task.Delay(500 * attempt);
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
        Console.WriteLine();
        Console.Write(prompt);
        var input = Console.ReadLine()?.Trim();
        return input is not null
               && (input.Equals("y", StringComparison.OrdinalIgnoreCase)
                   || input.Equals("yes", StringComparison.OrdinalIgnoreCase));
    }

    private static AppSettings LoadSettings(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddEnvironmentVariables()
            .AddCommandLine(args)
            .Build();

        var settings = configuration.Get<AppSettings>() ?? new AppSettings();

        if (string.IsNullOrWhiteSpace(settings.BillDotCom.DevKey)
            || string.IsNullOrWhiteSpace(settings.BillDotCom.Username)
            || string.IsNullOrWhiteSpace(settings.BillDotCom.OrganizationId))
        {
            throw new InvalidOperationException(
                "Bill.com credentials are incomplete. Check the 'BillDotCom' section in appsettings.json.");
        }

        return settings;
    }

    private static string ResolveExcelPath(string[] args, string configuredPath)
    {
        // Support: --excel <path>  or  --excel=<path>
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].Equals("--excel", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                return args[i + 1];
            }

            if (args[i].StartsWith("--excel=", StringComparison.OrdinalIgnoreCase))
            {
                return args[i]["--excel=".Length..];
            }
        }

        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            throw new InvalidOperationException(
                "No Excel path provided. Set Excel:Path in appsettings.json or pass --excel <path>.");
        }

        return configuredPath;
    }
}
