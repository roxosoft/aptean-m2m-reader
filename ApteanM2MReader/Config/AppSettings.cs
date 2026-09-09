namespace JetSolutions.ApteanM2MReader.Config;

public sealed class AppSettings
{
    public ApteanSettings Aptean { get; set; } = new();
    public OutputSettings Output { get; set; } = new();
}

public sealed class ApteanSettings
{
    public string BaseUrl { get; set; } = "https://apps.m2m.apteangovcloud.com";
    public string ContextPath { get; set; } = "webapi";
    public string ObjectName { get; set; } = "Vendor";
    public string CompanyId { get; set; } = string.Empty;
    public string Tenant { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
}

public sealed class OutputSettings
{
    /// <summary>Path for the Bill-sync-compatible vendors Excel file.</summary>
    public string Path { get; set; } = "../vendors.xlsx";
}
