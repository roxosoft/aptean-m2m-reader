using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using JetSolutions.ApteanM2MReader.Config;

namespace JetSolutions.ApteanM2MReader.Aptean;

/// <summary>
/// Obtains OAuth access tokens from the Made2Manage identity server
/// using the Client Credentials grant.
/// </summary>
public sealed class ApteanAuthClient : IDisposable
{
    private const string TokenPath = "idsrvapi/connect/token";
    private const string Scope = "M2MAPI";

    private readonly HttpClient _http;
    private readonly ApteanSettings _settings;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public ApteanAuthClient(ApteanSettings settings)
    {
        _settings = settings;

        var baseUrl = settings.BaseUrl.TrimEnd('/') + "/";
        _http = new HttpClient { BaseAddress = new Uri(baseUrl) };
        // Follow redirects (required by Aptean Web API docs).
        _http.DefaultRequestHeaders.Add("CompanyID", settings.CompanyId);
    }

    public async Task<TokenResponse> GetAccessTokenAsync(CancellationToken ct = default)
    {
        // Per Aptean docs: client_id / client_secret in the form body.
        var bodyToken = await RequestTokenAsync(includeClientInBody: true, useBasicAuth: false, ct);
        if (bodyToken is not null)
        {
            return bodyToken;
        }

        // Some IdentityServer setups expect HTTP Basic client authentication instead.
        var basicToken = await RequestTokenAsync(includeClientInBody: false, useBasicAuth: true, ct);
        if (basicToken is not null)
        {
            return basicToken;
        }

        throw new ApteanApiException(
            "Aptean token request failed with invalid_client. " +
            "Check APICONFIG Client Configuration: Client Name (ClientId) and Client Password (ClientSecret) " +
            "must match exactly, the client must be Enabled, Grant Type must be CLIENTCREDENTIALS, " +
            "and CompanyID must match the APICONFIG company (or 00). " +
            "Client Password rules: min 6 chars with upper, lower, digit, and special (not &, =, or space).");
    }

    private async Task<TokenResponse?> RequestTokenAsync(bool includeClientInBody, bool useBasicAuth, CancellationToken ct)
    {
        var fields = new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["scope"] = Scope,
            ["tenant"] = _settings.Tenant,
        };

        if (includeClientInBody)
        {
            fields["client_id"] = _settings.ClientId;
            fields["client_secret"] = _settings.ClientSecret;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, TokenPath)
        {
            Content = new FormUrlEncodedContent(fields),
        };

        if (useBasicAuth)
        {
            var raw = $"{_settings.ClientId}:{_settings.ClientSecret}";
            var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", encoded);
        }

        using var response = await _http.SendAsync(request, ct);
        var rawBody = await response.Content.ReadAsStringAsync(ct);

        if (response.IsSuccessStatusCode)
        {
            var token = JsonSerializer.Deserialize<TokenResponse>(rawBody, _jsonOptions);
            if (token?.AccessToken is null || token.AccessToken.Length == 0)
            {
                throw new ApteanApiException("Token request succeeded but no access_token was returned.");
            }

            return token;
        }

        // Soft-fail only for invalid_client so the alternate auth method can be tried.
        if ((int)response.StatusCode == 400
            && rawBody.Contains("invalid_client", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var body = string.IsNullOrWhiteSpace(rawBody) ? "(no response body)" : rawBody;
        throw new ApteanApiException(
            $"Aptean token request failed ({(int)response.StatusCode} {response.StatusCode}): {body}",
            response.StatusCode);
    }

    public void Dispose() => _http.Dispose();
}

public sealed class ApteanApiException : Exception
{
    public HttpStatusCode? StatusCode { get; }

    public ApteanApiException(string message, HttpStatusCode? statusCode = null) : base(message)
    {
        StatusCode = statusCode;
    }
}
