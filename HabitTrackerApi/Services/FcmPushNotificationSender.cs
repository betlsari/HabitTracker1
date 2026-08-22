using System.Net.Http.Headers;
using System.Text.Json;
using Data;
using Microsoft.EntityFrameworkCore;

namespace Services;

public interface IPushNotificationSender
{
    Task SendAsync(IReadOnlyList<string> deviceTokens, string title, string body, CancellationToken cancellationToken = default);
}

/// <summary>
/// DÜZELTİLDİ: Google'ın kapattığı FCM Legacy HTTP API (fcm.googleapis.com/fcm/send
/// + "key={serverKey}" header'ı) yerine FCM HTTP v1 API kullanılıyor. v1 API,
/// server key değil OAuth2 access token (servis hesabı üzerinden) gerektiriyor —
/// bkz. FcmAccessTokenProvider. Endpoint artık proje bazlı:
/// https://fcm.googleapis.com/v1/projects/{projectId}/messages:send ve her
/// mesaj ayrı ayrı, tek bir "message" nesnesiyle gönderiliyor (v1'de toplu
/// gönderim yok, batching gerekiyorsa Admin SDK'daki sendEachForMulticast
/// dengi ayrıca eklenmeli).
/// </summary>
public class FcmPushNotificationSender : IPushNotificationSender
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly FcmAccessTokenProvider _tokenProvider;
    private readonly ILogger<FcmPushNotificationSender> _logger;
    private readonly AppDbContext _context;

    public FcmPushNotificationSender(
        IHttpClientFactory httpClientFactory,
        FcmAccessTokenProvider tokenProvider,
        ILogger<FcmPushNotificationSender> logger,
        AppDbContext context)
    {
        _httpClientFactory = httpClientFactory;
        _tokenProvider = tokenProvider;
        _logger = logger;
        _context = context;
    }

    public async Task SendAsync(IReadOnlyList<string> deviceTokens, string title, string body, CancellationToken cancellationToken = default)
    {
        if (deviceTokens.Count == 0)
        {
            return;
        }

        if (!_tokenProvider.IsConfigured)
        {
            // FCM yapılandırılmamışsa (ör. local/dev ortamı) sessizce çık —
            // bildirim kaydı zaten UserNotifications tablosuna yazıldı, sadece
            // push gönderimi atlanıyor.
            return;
        }

        var accessToken = await _tokenProvider.GetAccessTokenAsync(cancellationToken);
        if (string.IsNullOrEmpty(accessToken))
        {
            _logger.LogWarning("FCM access token alınamadığı için push bildirimi gönderilemedi.");
            return;
        }

        var projectId = _tokenProvider.ProjectId;
        var endpoint = $"https://fcm.googleapis.com/v1/projects/{projectId}/messages:send";

        var client = _httpClientFactory.CreateClient(nameof(FcmPushNotificationSender));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        foreach (var token in deviceTokens)
        {
            var payload = new
            {
                message = new
                {
                    token,
                    notification = new { title, body },
                    data = new Dictionary<string, string>
                    {
                        ["title"] = title,
                        ["body"] = body
                    }
                }
            };

            try
            {
                var response = await client.PostAsJsonAsync(endpoint, payload, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                    var errorCode = TryExtractFcmErrorCode(errorBody);
                    if (response.StatusCode == System.Net.HttpStatusCode.NotFound ||
                        string.Equals(errorCode, "UNREGISTERED", StringComparison.OrdinalIgnoreCase))
                    {
                        var removed = await _context.DeviceTokens
                            .Where(t => t.Token == token)
                            .ExecuteDeleteAsync(cancellationToken);
                        _logger.LogInformation("Geçersiz FCM token temizlendi. Removed={Removed}", removed);
                    }

                    _logger.LogWarning(
                        "FCM gönderimi başarısız. Status={Status} ErrorCode={ErrorCode} Body={Body}",
                        response.StatusCode, errorCode, errorBody);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "FCM gönderimi sırasında hata oluştu.");
            }
        }
    }

    private static string? TryExtractFcmErrorCode(string errorBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(errorBody);
            if (doc.RootElement.TryGetProperty("error", out var errorEl) &&
                errorEl.TryGetProperty("details", out var detailsEl) &&
                detailsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var detail in detailsEl.EnumerateArray())
                {
                    if (detail.TryGetProperty("errorCode", out var codeEl))
                    {
                        return codeEl.GetString();
                    }
                }
            }
        }
        catch
        {
            // Parse edilemezse sorun değil, sadece ham body loglanır.
        }

        return null;
    }
}
