namespace JetSolutions.BillVendorSync.Excel;

/// <summary>A single M2M vendor (from Aptean API or Excel export).</summary>
public sealed record M2MVendor
{
    public required string M2MVendorId { get; init; }
    public required string VendorName { get; init; }
    public string? StreetAddress { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public string? ZipCode { get; init; }
    public string? Country { get; init; }
    public string? Phone { get; init; }
    public string? Email { get; init; }

    /// <summary>1-based row number in the worksheet (for diagnostics).</summary>
    public int RowNumber { get; init; }
}
