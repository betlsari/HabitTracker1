using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;

namespace Services;

/// <summary>
/// YENİ: FCM Legacy HTTP API (fcm.googleapis.com/fcm/send + server key) Google
/// tarafından kapatıldı. Bu sınıf, FCM HTTP v1 API için gereken OAuth2 access
/// token'ı bir servis hesabı (service account) JSON'undan üretir:
///   1) Servis hesabının private key'i ile RS256 imzalı bir JWT (self-signed
///      assertion) oluşturulur.
///   2) Bu JWT, https://oauth2.googleapis.com/token adresine
///      grant_type=urn:ietf:params:oauth:grant-type:jwt-bearer ile POST edilerek
///      gerçek bir OAuth2 access_token ile değiştirilir.
///   3) Access token'lar ~1 saat geçerli olduğundan, süresi dolana kadar
///      bellekte (thread-safe) önbelleğe alınır; her push gönderiminde yeniden
///      token almak gereksiz gecikme/gereksiz istek yaratır.
/// Singleton olarak kayıtlı olmalı (bkz. Program.cs) — token cache'inin tüm
/// request'ler arasında paylaşılması gerekiyor.
/// </summary>
public class FcmAccessTokenProvider
{
    private const string TokenUri = "https://oauth2.googleapis.com/token";
    private const string MessagingScope = "https://www.googleapis.com/auth/firebase.messaging";

    // Google token'ı genelde 3600 sn geçerli oluyor; erken yenilemek için pay bırakıyoruz.
    private static readonly TimeSpan RefreshSkew = TimeSpan.FromMinutes(5);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<FcmAccessTokenProvider> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private string? _cachedAccessToken;
    private DateTimeOffset _cachedTokenExpiresAt = DateTimeOffset.MinValue;
    private ServiceAccountCredential? _credential;

    public FcmAccessTokenProvider(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<FcmAccessTokenProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Konfigürasyondan okunan Fcm:ProjectId. Boşsa FCM gönderimi devre dışı sayılır.
    /// </summary>
    public string? ProjectId => _configuration["Fcm:ProjectId"];

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ProjectId) && TryLoadCredential() != null;

    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(_cachedAccessToken) && DateTimeOffset.UtcNow < _cachedTokenExpiresAt - RefreshSkew)
        {
            return _cachedAccessToken;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            // Çift kontrol: kilidi beklerken başka bir istek zaten yenilemiş olabilir.
            if (!string.IsNullOrEmpty(_cachedAccessToken) && DateTimeOffset.UtcNow < _cachedTokenExpiresAt - RefreshSkew)
            {
                return _cachedAccessToken;
            }

            var credential = TryLoadCredential();
            if (credential == null)
            {
                return null;
            }

            var assertion = BuildSignedJwtAssertion(credential);

            var client = _httpClientFactory.CreateClient(nameof(FcmAccessTokenProvider));
            var requestContent = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
                ["assertion"] = assertion
            });

            var response = await client.PostAsync(TokenUri, requestContent, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("FCM OAuth2 token alınamadı. Status={Status} Body={Body}", response.StatusCode, body);
                return null;
            }

            using var doc = JsonDocument.Parse(body);
            var accessToken = doc.RootElement.GetProperty("access_token").GetString();
            var expiresInSeconds = doc.RootElement.TryGetProperty("expires_in", out var expEl)
                ? expEl.GetInt32()
                : 3600;

            _cachedAccessToken = accessToken;
            _cachedTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresInSeconds);

            return _cachedAccessToken;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FCM OAuth2 token üretimi sırasında hata oluştu.");
            return null;
        }
        finally
        {
            _lock.Release();
        }
    }

    private ServiceAccountCredential? TryLoadCredential()
    {
        if (_credential != null)
        {
            return _credential;
        }

        // Servis hesabı JSON'u iki şekilde sağlanabilir:
        //  1) Fcm:ServiceAccountJson  -> JSON içeriğinin doğrudan kendisi
        //     (Docker/K8s secret veya ortam değişkeni olarak enjekte etmek için ideal).
        //  2) Fcm:ServiceAccountJsonPath -> JSON dosyasının diskteki yolu.
        var rawJson = _configuration["Fcm:ServiceAccountJson"];
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            var path = _configuration["Fcm:ServiceAccountJsonPath"];
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return null;
            }
            rawJson = File.ReadAllText(path);
        }

        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;
            var clientEmail = root.GetProperty("client_email").GetString();
            var privateKey = root.GetProperty("private_key").GetString();
            var tokenUri = root.TryGetProperty("token_uri", out var tuEl) ? tuEl.GetString() : TokenUri;

            if (string.IsNullOrWhiteSpace(clientEmail) || string.IsNullOrWhiteSpace(privateKey))
            {
                _logger.LogError("FCM servis hesabı JSON'u eksik alan içeriyor (client_email/private_key).");
                return null;
            }

            _credential = new ServiceAccountCredential(clientEmail, privateKey, tokenUri ?? TokenUri);
            return _credential;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FCM servis hesabı JSON'u parse edilemedi.");
            return null;
        }
    }

    private static string BuildSignedJwtAssertion(ServiceAccountCredential credential)
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(credential.PrivateKey);
        var securityKey = new RsaSecurityKey(rsa) { KeyId = null };
        var signingCredentials = new SigningCredentials(securityKey, SecurityAlgorithms.RsaSha256);

        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(
            issuer: credential.ClientEmail,
            audience: credential.TokenUri,
            claims: new[] { new Claim("scope", MessagingScope) },
            notBefore: now,
            expires: now.AddHours(1),
            signingCredentials: signingCredentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private sealed record ServiceAccountCredential(string ClientEmail, string PrivateKey, string TokenUri);
}