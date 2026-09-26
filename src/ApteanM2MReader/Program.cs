using Azure.Identity;
using Azure.Storage.Blobs;
using JetSolutions.ApteanM2MReader.Aptean;
using JetSolutions.ApteanM2MReader.Config;
using JetSolutions.ApteanM2MReader.Excel;
using JetSolutions.ApteanM2MReader.Storage;
using JetSolutions.BillVendorSync;
using JetSolutions.BillVendorSync.Excel;
using Microsoft.Extensions.Configuration;

namespace JetSolutions.ApteanM2MReader;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        try
        {
            var settings = LoadSettings(args);

            Console.WriteLine($"Requesting Aptean M2M access token from {settings.Aptean.BaseUrl}...");
            Console.WriteLine($"  CompanyId={settings.Aptean.CompanyId}, Tenant={settings.Aptean.Tenant}, ClientId={settings.Aptean.ClientId}");

            using var authClient = new ApteanAuthClient(settings.Aptean);
            var token = await authClient.GetAccessTokenAsync();

            var prefix = token.AccessToken!.Length <= 8
                ? token.AccessToken
                : token.AccessToken[..8];

            Console.WriteLine("Login successful.");
            Console.WriteLine($"  token_type : {token.TokenType}");
            Console.WriteLine($"  expires_in : {token.ExpiresIn}s");
            Console.WriteLine($"  token      : {prefix}...");

            var listUrl =
                $"{settings.Aptean.BaseUrl.TrimEnd('/')}/{settings.Aptean.ContextPath.Trim('/')}/api/{settings.Aptean.ObjectName.Trim('/')}";
            Console.WriteLine();
            Console.WriteLine($"Fetching vendors from {listUrl}...");

            using var apiClient = new ApteanApiClient(settings.Aptean);
            var fetch = await apiClient.GetAllVendorsAsync(token.AccessToken);

            if (fetch.Vendors.Count == 0)
            {
                Console.WriteLine("Warning: API returned no vendors. Writing Excel with headers only.");
            }
            else
            {
                Console.WriteLine(
                    $"Fetched {fetch.Vendors.Count} vendors across {fetch.PagesFetched} list page(s); " +
                    $"enriched {fetch.DetailsFetched} via detail GET.");
            }

            var outputPath = ResolveOutputPath(args, settings.Output.Path);
            VendorExcelWriter.Write(outputPath, fetch.Vendors);
            Console.WriteLine($"Wrote Excel: {Path.GetFullPath(outputPath)}");

            if (!string.IsNullOrWhiteSpace(settings.AzureStorage.AccountName)
                && !string.IsNullOrWhiteSpace(settings.AzureStorage.ContainerName))
            {
                var blobName = $"vendors-{DateTime.UtcNow:yyyyMMdd-HHmmss}.xlsx";
                var blobClient = new BlobServiceClient(
                    new Uri($"https://{settings.AzureStorage.AccountName}.blob.core.windows.net"),
                    new DefaultAzureCredential());
                var uploader = new VendorBlobUploader(blobClient);
                var uri = await uploader.UploadAsync(
                    settings.AzureStorage.ContainerName,
                    blobName,
                    outputPath);
                Console.WriteLine($"Uploaded blob: {uri}");
            }

            foreach (var sample in fetch.Vendors.Take(3))
            {
                Console.WriteLine(
                    $"  sample: {sample.VendorId} | {sample.Company} | {sample.City} | {sample.State} | {sample.ZipCode} | {sample.Phone}");
            }

            if (ShouldRunBillSync(settings))
            {
                Console.WriteLine();
                var m2mVendors = MapToM2MVendors(fetch.Vendors);
                bool production = IsProduction();
                bool autoExact = settings.BillSync.AutoApplyExact || production;
                bool autoRelaxed = settings.BillSync.AutoApplyRelaxed;

                await VendorSyncPipeline.RunAsync(
                    m2mVendors,
                    settings.BillDotCom,
                    autoApplyExact: autoExact,
                    autoApplyRelaxed: autoRelaxed,
                    reportsDirectory: "reports");
            }
            else
            {
                Console.WriteLine();
                Console.WriteLine("Bill.com sync skipped (BillDotCom credentials missing or BillSync:Enabled=false).");
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static IReadOnlyList<M2MVendor> MapToM2MVendors(IReadOnlyList<ApteanVendor> vendors)
    {
        var list = new List<M2MVendor>(vendors.Count);
        for (int i = 0; i < vendors.Count; i++)
        {
            var v = vendors[i];
            list.Add(new M2MVendor
            {
                M2MVendorId = v.VendorId,
                VendorName = v.Company,
                City = v.City,
                ZipCode = v.ZipCode,
                RowNumber = i + 1,
            });
        }

        return list;
    }

    private static bool ShouldRunBillSync(AppSettings settings)
    {
        if (settings.BillSync.Enabled == false)
        {
            return false;
        }

        var b = settings.BillDotCom;
        bool hasCreds = !string.IsNullOrWhiteSpace(b.DevKey)
            && !string.IsNullOrWhiteSpace(b.Username)
            && !string.IsNullOrWhiteSpace(b.OrganizationId)
            && !string.IsNullOrWhiteSpace(b.Password);

        if (settings.BillSync.Enabled == true)
        {
            if (!hasCreds)
            {
                throw new InvalidOperationException(
                    "BillSync:Enabled is true but BillDotCom credentials are incomplete.");
            }

            return true;
        }

        // Enabled unset: run when credentials are present.
        return hasCreds;
    }

    private static bool IsProduction()
    {
        var env = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? "";
        return env.Equals("Production", StringComparison.OrdinalIgnoreCase);
    }

    private static AppSettings LoadSettings(string[] args)
    {
        var environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production";

        var configurationBuilder = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables();

        if (!environment.Equals("Development", StringComparison.OrdinalIgnoreCase))
        {
            var keyVaultName = Environment.GetEnvironmentVariable("KeyVault__Name");
            if (!string.IsNullOrWhiteSpace(keyVaultName))
            {
                configurationBuilder.AddAzureKeyVault(
                    new Uri($"https://{keyVaultName}.vault.azure.net/"),
                    new DefaultAzureCredential());
            }
        }

        var configuration = configurationBuilder
            .AddEnvironmentVariables()
            .AddCommandLine(args)
            .Build();

        var settings = configuration.Get<AppSettings>() ?? new AppSettings();
        var a = settings.Aptean;

        if (string.IsNullOrWhiteSpace(a.BaseUrl)
            || string.IsNullOrWhiteSpace(a.CompanyId)
            || string.IsNullOrWhiteSpace(a.Tenant)
            || string.IsNullOrWhiteSpace(a.ClientId)
            || string.IsNullOrWhiteSpace(a.ClientSecret))
        {
            throw new InvalidOperationException(
                "Aptean credentials are incomplete. Copy appsettings.example.json to appsettings.json " +
                "and fill the 'Aptean' section (BaseUrl, CompanyId, Tenant, ClientId, ClientSecret), " +
                "or set environment variables / Key Vault secrets.");
        }

        if (string.IsNullOrWhiteSpace(a.ContextPath))
        {
            a.ContextPath = "webapi";
        }

        if (string.IsNullOrWhiteSpace(a.ObjectName))
        {
            a.ObjectName = "Vendor";
        }

        if (string.IsNullOrWhiteSpace(settings.Output.Path))
        {
            settings.Output.Path = "vendors.xlsx";
        }

        return settings;
    }

    private static string ResolveOutputPath(string[] args, string configuredPath)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--output", StringComparison.OrdinalIgnoreCase)
                || string.Equals(args[i], "--excel", StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        if (!Path.IsPathRooted(configuredPath))
        {
            return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), configuredPath));
        }

        return configuredPath;
    }
}
