namespace JetSolutions.BillVendorSync.Excel;

/// <summary>A single vendor row read from the M2M Excel export.</summary>
public sealed record M2MVendor
{
    public required string M2MVendorId { get; init; }
    public required string VendorName { get; init; }
    public string? City { get; init; }
    public string? ZipCode { get; init; }

    /// <summary>1-based row number in the worksheet (for diagnostics).</summary>
    public int RowNumber { get; init; }
}
