using System.Globalization;
using System.Text;
using JetSolutions.BillVendorSync.Matching;

namespace JetSolutions.BillVendorSync.Reporting;

public enum SyncAction
{
    None = 0,
    Update = 1,
    Create = 2,
}

/// <summary>Outcome of an attempted Bill.com update or create.</summary>
public sealed class UpdateOutcome
{
    public required MatchResult Result { get; init; }
    public SyncAction Action { get; set; } = SyncAction.Update;
    public bool Applied { get; set; }
    public string? CreatedBillVendorId { get; set; }
    public string? Error { get; set; }

    /// <summary>Non-fatal note attached to a successful update/create (e.g. placeholder address).</summary>
    public string? Warning { get; set; }
}

public static class ReportWriter
{
    public static void PrintPreview(IReadOnlyList<MatchResult> results)
    {
        var exactUpdate = results.Where(r => r.Status == MatchStatus.Matched && r.MatchType == VendorMatchType.Exact).ToList();
        var relaxedUpdate = results.Where(r => r.Status == MatchStatus.Matched && r.MatchType == VendorMatchType.Relaxed).ToList();
        var alreadySet = results.Where(r => r.Status == MatchStatus.AlreadySet).ToList();
        var ambiguous = results.Where(r => r.Status == MatchStatus.Ambiguous).ToList();
        var willCreate = results.Where(r => r.Status == MatchStatus.Unmatched && r.M2M is not null).ToList();
        var unmatchedBill = results.Where(r => r.Status == MatchStatus.Unmatched && r.M2M is null).ToList();

        Console.WriteLine();
        Console.WriteLine("=== Match preview ===");
        Console.WriteLine($"{"VendorName",-40} {"BillVendorId",-22} {"M2MVendorID",-16} {"CurrentCompany",-14} Action");
        Console.WriteLine(new string('-', 110));

        foreach (var r in exactUpdate)
        {
            PrintRow(r.BillVendor?.Name, r.BillVendor?.Id, r.M2M?.M2MVendorId, r.BillVendor?.AdditionalInfo?.CompanyName, "Will update (exact)");
        }

        foreach (var r in relaxedUpdate)
        {
            PrintRow(r.BillVendor?.Name, r.BillVendor?.Id, r.M2M?.M2MVendorId, r.BillVendor?.AdditionalInfo?.CompanyName, $"Will update (relaxed: M2M '{r.M2M?.VendorName}')");
        }

        foreach (var r in alreadySet)
        {
            PrintRow(r.BillVendor?.Name, r.BillVendor?.Id, r.M2M?.M2MVendorId, r.BillVendor?.AdditionalInfo?.CompanyName, $"Skip (already set, {r.MatchType.ToString().ToLowerInvariant()})");
        }

        foreach (var r in ambiguous)
        {
            PrintRow(r.M2M?.VendorName, $"({r.Candidates.Count} candidates)", r.M2M?.M2MVendorId, null, $"Ambiguous ({r.MatchType.ToString().ToLowerInvariant()})");
        }

        foreach (var r in willCreate)
        {
            PrintRow(r.M2M?.VendorName, null, r.M2M?.M2MVendorId, null, "Will create");
        }

        Console.WriteLine();
        Console.WriteLine("=== Preview summary ===");
        Console.WriteLine($"  Will update (exact)  : {exactUpdate.Count}");
        Console.WriteLine($"  Will update (relaxed): {relaxedUpdate.Count}");
        Console.WriteLine($"  Will create          : {willCreate.Count}");
        Console.WriteLine($"  Skip (already set)   : {alreadySet.Count}");
        Console.WriteLine($"  Ambiguous            : {ambiguous.Count}");
        Console.WriteLine($"  Unmatched (Bill.com) : {unmatchedBill.Count}");
    }

    private static void PrintRow(string? name, string? billId, string? m2mId, string? currentAcct, string action)
    {
        Console.WriteLine($"{Trunc(name, 40),-40} {Trunc(billId, 22),-22} {Trunc(m2mId, 16),-16} {Trunc(currentAcct, 14),-14} {action}");
    }

    public static void PrintFinalSummary(IReadOnlyList<UpdateOutcome> outcomes, IReadOnlyList<MatchResult> allResults)
    {
        int updatedExact = outcomes.Count(o => o.Applied && o.Action == SyncAction.Update && o.Result.MatchType == VendorMatchType.Exact);
        int updatedRelaxed = outcomes.Count(o => o.Applied && o.Action == SyncAction.Update && o.Result.MatchType == VendorMatchType.Relaxed);
        int created = outcomes.Count(o => o.Applied && o.Action == SyncAction.Create);
        int placeholderAddr = outcomes.Count(o => o.Applied && !string.IsNullOrWhiteSpace(o.Warning));
        int failedUpdates = outcomes.Count(o => !o.Applied && o.Action == SyncAction.Update);
        int failedCreates = outcomes.Count(o => !o.Applied && o.Action == SyncAction.Create);
        int skipped = allResults.Count(r => r.Status == MatchStatus.AlreadySet);
        int ambiguous = allResults.Count(r => r.Status == MatchStatus.Ambiguous);
        int unmatchedBill = allResults.Count(r => r.Status == MatchStatus.Unmatched && r.M2M is null);
        int willCreateTotal = allResults.Count(r => r.Status == MatchStatus.Unmatched && r.M2M is not null);
        int createAttempts = outcomes.Count(o => o.Action == SyncAction.Create);
        int unmatchedM2MLeft = Math.Max(0, willCreateTotal - createAttempts);
        Console.WriteLine();
        Console.WriteLine("=== Final summary ===");
        Console.WriteLine($"  Updated (exact)      : {updatedExact}");
        Console.WriteLine($"  Updated (relaxed)    : {updatedRelaxed}");
        Console.WriteLine($"  Created              : {created}");
        Console.WriteLine($"  ...incl. placeholder : {placeholderAddr}");
        Console.WriteLine($"  Failed (updates)     : {failedUpdates}");
        Console.WriteLine($"  Failed (creates)     : {failedCreates}");
        Console.WriteLine($"  Skipped (already set): {skipped}");
        Console.WriteLine($"  Ambiguous            : {ambiguous}");
        Console.WriteLine($"  Unmatched M2M (left) : {unmatchedM2MLeft}");
        Console.WriteLine($"  Unmatched (Bill.com) : {unmatchedBill}");
    }

    public static string WriteCsv(
        IReadOnlyList<MatchResult> results,
        IReadOnlyDictionary<MatchResult, UpdateOutcome> outcomes,
        string directory)
    {
        Directory.CreateDirectory(directory);
        var fileName = $"sync-report-{DateTime.Now:yyyyMMdd-HHmmss}.csv";
        var path = Path.Combine(directory, fileName);

        var sb = new StringBuilder();
        sb.AppendLine("Status,MatchType,Action,VendorName,M2MVendorName,BillVendorId,M2MVendorID,CurrentCompanyName,Applied,Error,Note");

        foreach (var r in results)
        {
            outcomes.TryGetValue(r, out var outcome);
            string applied = outcome is null ? string.Empty : outcome.Applied ? "true" : "false";
            string error = outcome?.Error ?? string.Empty;
            string action = outcome is null || outcome.Action == SyncAction.None
                ? string.Empty
                : outcome.Action.ToString();
            string billId = outcome?.CreatedBillVendorId ?? r.BillVendor?.Id ?? string.Empty;
            string vendorName = r.BillVendor?.Name ?? r.M2M?.VendorName ?? string.Empty;

            var note = string.Join(" ", new[] { r.Note, outcome?.Warning }.Where(s => !string.IsNullOrWhiteSpace(s)));

            sb.Append(Csv(r.Status.ToString())).Append(',')
              .Append(Csv(r.MatchType.ToString())).Append(',')
              .Append(Csv(action)).Append(',')
              .Append(Csv(vendorName)).Append(',')
              .Append(Csv(r.M2M?.VendorName)).Append(',')
              .Append(Csv(billId)).Append(',')
              .Append(Csv(r.M2M?.M2MVendorId)).Append(',')
              .Append(Csv(r.BillVendor?.AdditionalInfo?.CompanyName)).Append(',')
              .Append(Csv(applied)).Append(',')
              .Append(Csv(error)).Append(',')
              .Append(Csv(note))
              .AppendLine();
        }

        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        return path;
    }

    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        bool mustQuote = value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r');
        var escaped = value.Replace("\"", "\"\"");
        return mustQuote ? $"\"{escaped}\"" : escaped;
    }

    private static string Trunc(string? value, int max)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Length <= max ? value : value[..(max - 1)] + "…";
    }
}
