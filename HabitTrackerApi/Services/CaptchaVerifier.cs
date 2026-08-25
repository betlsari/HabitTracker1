using System.Net.Http.Json;
using Configuration;
using Microsoft.Extensions.Options;

namespace Services;

public sealed class CaptchaVerifier
{
    private readonly HttpClient _httpClient;
    private readonly CaptchaOptions _options;

    public CaptchaVerifier(HttpClient httpClient, IOptions<CaptchaOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<bool> ValidateAsync(string? token, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled || string.IsNullOrWhiteSpace(token))
        {
            return !_options.Enabled;
        }

        if (string.IsNullOrWhiteSpace(_options.Secret) || string.IsNullOrWhiteSpace(_options.VerifyUrl))
        {
            return false;
        }

        var payload = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("secret", _options.Secret),
            new KeyValuePair<string, string>("response", token)
        });

        using var response = await _httpClient.PostAsync(_options.VerifyUrl, payload, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return false;
        }

        var result = await response.Content.ReadFromJsonAsync<CaptchaVerifyResponse>(cancellationToken: cancellationToken);
        return result?.Success == true;
    }

    private sealed class CaptchaVerifyResponse
    {
        public bool Success { get; set; }
    }
}
