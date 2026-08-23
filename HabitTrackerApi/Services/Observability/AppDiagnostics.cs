using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Services.Observability;


public static class AppDiagnostics
{
    public const string ServiceName = "HabitTrackerApi";

    public static readonly ActivitySource ActivitySource = new(ServiceName);
    public static readonly Meter Meter = new(ServiceName);

    public static readonly Counter<long> RecalculationSucceeded = Meter.CreateCounter<long>(
        "habittracker.recalculation.succeeded",
        description: "Başarıyla tamamlanan arka plan yeniden hesaplama (habit/kitap) sayısı.");

    public static readonly Counter<long> RecalculationFailed = Meter.CreateCounter<long>(
        "habittracker.recalculation.failed",
        description: "Tüm denemelerden sonra başarısız kalan arka plan yeniden hesaplama sayısı.");

    public static readonly Histogram<double> RecalculationDurationMs = Meter.CreateHistogram<double>(
        "habittracker.recalculation.duration",
        unit: "ms",
        description: "Bir yeniden hesaplama işinin (habit veya kitap) işlenme süresi.");
}