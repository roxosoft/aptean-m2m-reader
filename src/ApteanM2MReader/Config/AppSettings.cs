using JetSolutions.BillVendorSync.Config;

namespace JetSolutions.ApteanM2MReader.Config;

public sealed class AppSettings
{
    public ApteanSettings Aptean { get; set; } = new();
    public OutputSettings Output { get; set; } = new();
    public AzureStorageSettings AzureStorage { get; set; } = new();
    public BillDotComSettings BillDotCom { get; set; } = new();
    public BillSyncSettings BillSync { get; set; } = new();
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
    public string Path { get; set; } = "vendors.xlsx";
}

public sealed class AzureStorageSettings
{
    public string AccountName { get; set; } = string.Empty;
    public string ContainerName { get; set; } = string.Empty;
}

/// <summary>Controls whether ApteanM2MReader also pushes matches into Bill.com.</summary>
public sealed class BillSyncSettings
{
    /// <summary>When null, Bill sync runs if BillDotCom credentials are present.</summary>
    public bool? Enabled { get; set; }

    /// <summary>Apply exact matches without Y/N (required for Azure scheduled jobs).</summary>
    public bool AutoApplyExact { get; set; }

    /// <summary>Apply relaxed matches without Y/N (also forced on in Production).</summary>
    public bool AutoApplyRelaxed { get; set; }
}
