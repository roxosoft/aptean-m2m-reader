using JetSolutions.BillVendorSync.BillDotCom;
using JetSolutions.BillVendorSync.Excel;

namespace JetSolutions.BillVendorSync.Matching;

public enum MatchStatus
{
    /// <summary>A single Bill.com vendor was matched and its companyName will be set.</summary>
    Matched,

    /// <summary>Matched, but the Bill.com vendor already has a non-empty companyName (skipped, flagged).</summary>
    AlreadySet,

    /// <summary>More than one plausible candidate remained after the City/Zip tiebreaker (skipped).</summary>
    Ambiguous,

    /// <summary>No Bill.com vendor matched this M2M row, or vice versa (report only).</summary>
    Unmatched,
}

/// <summary>How confidently the match was made.</summary>
public enum VendorMatchType
{
    /// <summary>Names agreed after basic normalization and legal-suffix stripping.</summary>
    Exact,

    /// <summary>Names agreed only after relaxed normalization (articles/TLDs/noise words) or subset matching.</summary>
    Relaxed,
}

public sealed class MatchResult
{
    public required MatchStatus Status { get; init; }

    public VendorMatchType MatchType { get; init; } = VendorMatchType.Exact;

    public M2MVendor? M2M { get; init; }
    public VendorResponseDto? BillVendor { get; init; }

    /// <summary>For ambiguous cases, the candidates that could not be disambiguated.</summary>
    public IReadOnlyList<VendorResponseDto> Candidates { get; init; } = Array.Empty<VendorResponseDto>();

    public string? Note { get; init; }

    public bool WillUpdate => Status == MatchStatus.Matched;
}
