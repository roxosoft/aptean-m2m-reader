namespace JetSolutions.ApteanM2MReader.Aptean;

/// <summary>A vendor row mapped from the M2M Web API JSON response.</summary>
public sealed record ApteanVendor
{
    public required string VendorId { get; init; }
    public required string Company { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public string? ZipCode { get; init; }
    public string? Phone { get; init; }
}

/// <summary>Result of fetching all vendor pages (and optional detail enrichment).</summary>
public sealed class VendorFetchResult
{
    public required IReadOnlyList<ApteanVendor> Vendors { get; init; }
    public int PagesFetched { get; init; }
    public int DetailsFetched { get; init; }
}
