using System.Text.Json.Serialization;

namespace JetSolutions.BillVendorSync.BillDotCom;

public sealed class LoginRequest
{
    [JsonPropertyName("devKey")]
    public string DevKey { get; set; } = string.Empty;

    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;

    [JsonPropertyName("password")]
    public string Password { get; set; } = string.Empty;

    [JsonPropertyName("organizationId")]
    public string OrganizationId { get; set; } = string.Empty;
}

public sealed class LoginResponse
{
    [JsonPropertyName("sessionId")]
    public string? SessionId { get; set; }

    [JsonPropertyName("organizationId")]
    public string? OrganizationId { get; set; }

    [JsonPropertyName("userId")]
    public string? UserId { get; set; }
}

public sealed class VendorListResponse
{
    [JsonPropertyName("results")]
    public List<VendorResponseDto> Results { get; set; } = new();

    [JsonPropertyName("nextPage")]
    public string? NextPage { get; set; }

    [JsonPropertyName("prevPage")]
    public string? PrevPage { get; set; }
}

public sealed class VendorResponseDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("accountNumber")]
    public string? AccountNumber { get; set; }

    [JsonPropertyName("archived")]
    public bool Archived { get; set; }

    [JsonPropertyName("address")]
    public AddressDto? Address { get; set; }

    [JsonPropertyName("additionalInfo")]
    public AdditionalInfoDto? AdditionalInfo { get; set; }
}

public sealed class AdditionalInfoDto
{
    [JsonPropertyName("companyName")]
    public string? CompanyName { get; set; }

    [JsonPropertyName("taxId")]
    public string? TaxId { get; set; }

    [JsonPropertyName("taxIdType")]
    public string? TaxIdType { get; set; }
}

public sealed class AddressDto
{
    [JsonPropertyName("line1")]
    public string? Line1 { get; set; }

    [JsonPropertyName("city")]
    public string? City { get; set; }

    [JsonPropertyName("stateOrProvince")]
    public string? StateOrProvince { get; set; }

    [JsonPropertyName("zipOrPostalCode")]
    public string? ZipOrPostalCode { get; set; }

    [JsonPropertyName("country")]
    public string? Country { get; set; }
}

public sealed class VendorUpdateRequest
{
    [JsonPropertyName("additionalInfo")]
    public AdditionalInfoDto AdditionalInfo { get; set; } = new();

    /// <summary>Optional. Only sent when retrying a record whose stored address is incomplete.</summary>
    [JsonPropertyName("address")]
    public AddressDto? Address { get; set; }
}

public sealed class BdcError
{
    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("severity")]
    public string? Severity { get; set; }

    [JsonPropertyName("category")]
    public string? Category { get; set; }
}
