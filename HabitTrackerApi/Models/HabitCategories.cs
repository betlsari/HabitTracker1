namespace Models;

public static class HabitCategories
{
    public const string Water = "Su";
    public const string Reading = "Kitap";
    public const string Focus = "Odaklanma";
    public const string Sport = "Spor";
    public const string Other = "Diğer";

    // YENİ: Category artık serbest metin değil, sabit bir whitelist'e karşı
    // doğrulanıyor (PetTypes.Allowed ile aynı desen). Böylece "Su İçme" gibi
    // varyasyonlar sessizce çiçek/pet/rozet mantığını devre dışı bırakamaz —
    // istemci sadece bu listedeki değerlerden birini gönderebilir.
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