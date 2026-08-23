namespace Models;

public static class HabitCategories
{
    public const string Water = "Su";
    public const string Reading = "Kitap";
    public const string Focus = "Odaklanma";
    public const string Sport = "Spor";
    public const string Other = "Diğer";

    // DÜZELTİLDİ (madde 8): "Water"/"Reading"/"Focus"/"Sport"/"Other" gibi
    // İngilizce alias'lar kaldırıldı. Önceden IsValid bunları sessizce kabul
    // ediyordu ama GET /api/habits/categories sadece Türkçe Allowed listesini
    // döndürüyordu; istemci geliştiricisi "gerçekten kabul edilen küme"yi bu
    // endpoint'ten öğrenemiyordu. Artık kabul edilen küme == endpoint'in
    // döndürdüğü küme (tek doğruluk kaynağı: Allowed).
    public static readonly string[] Allowed =
    {
        Water,
        Reading,
        Focus,
        Sport,
        Other
    };

    public static bool IsValid(string? category) =>
        !string.IsNullOrWhiteSpace(category) &&
        Allowed.Contains(category, StringComparer.OrdinalIgnoreCase);

    public static bool IsWater(string? category) => Matches(category, Water);

    public static bool IsReading(string? category) => Matches(category, Reading);

    public static bool IsFocus(string? category) => Matches(category, Focus);

    private static bool Matches(string? category, string target) =>
        !string.IsNullOrWhiteSpace(category) &&
        category.Equals(target, StringComparison.OrdinalIgnoreCase);
}