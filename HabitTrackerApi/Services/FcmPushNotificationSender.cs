namespace Services;

public interface IPushNotificationSender
{
    Task SendAsync(IReadOnlyList<string> deviceTokens, string title, string body, CancellationToken cancellationToken = default);
}

public class FcmPushNotificationSender : IPushNotificationSender
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<FcmPushNotificationSender> _logger;

    public FcmPushNotificationSender(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<FcmPushNotificationSender> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task SendAsync(IReadOnlyList<string> deviceTokens, string title, string body, CancellationToken cancellationToken = default)
    {
        var serverKey = _configuration["Fcm:ServerKey"];
        if (string.IsNullOrWhiteSpace(serverKey) || deviceTokens.Count == 0)
        {
            return;
        }

        var client = _httpClientFactory.CreateClient(nameof(FcmPushNotificationSender));
        client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"key={serverKey}");

        foreach (var token in deviceTokens)
        {
            var payload = new
            {
                to = token,
                notification = new { title, body },
                data = new { title, body }
            };

            try
            {
                var response = await client.PostAsJsonAsync("https://fcm.googleapis.com/fcm/send", payload, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("FCM gönderimi başarısız. Status={Status}", response.StatusCode);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "FCM gönderimi sırasında hata oluştu.");
            }
        }
    }
}
