namespace JetSolutions.BillVendorSync.Config;

public sealed class AppSettings
{
    public BillDotComSettings BillDotCom { get; set; } = new();
    public ExcelSettings Excel { get; set; } = new();
}

public sealed class BillDotComSettings
{
    public string BaseUrl { get; set; } = "https://gateway.stage.bill.com/connect/v3/";
    public string DevKey { get; set; } = string.Empty;
    public string OrganizationId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public sealed class ExcelSettings
{
    public string Path { get; set; } = string.Empty;

    /// <summary>Optional worksheet name. When empty, the first worksheet is used.</summary>
    public string SheetName { get; set; } = string.Empty;
}
