namespace Services;


public class PetMoodBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PetMoodBackgroundService> _logger;
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    public PetMoodBackgroundService(IServiceScopeFactory scopeFactory, ILogger<PetMoodBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var petMoodService = scope.ServiceProvider.GetRequiredService<PetMoodService>();
                await petMoodService.RecalculateMoodForAllUsersAsync(stoppingToken);
                _logger.LogInformation("Pet mood güncellemesi tamamlandı. {Time}", DateTime.UtcNow);
            }
            catch (Exception ex)
            {
                
                _logger.LogError(ex, "Pet mood güncelleme sırasında hata oluştu.");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                
            }
        }
    }
}
