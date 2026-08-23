namespace Models;

public static class HabitCategories
{
    public const string Water = "Su";
    public const string Reading = "Kitap";
    public const string Focus = "Odaklanma";
    public const string Sport = "Spor";
    public const string Other = "Diğer";

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

    // YENİ (madde 10): IsValid case-insensitive çalışıyor ("su"/"SU"/"Su"
    // hepsi geçerli) ama önceden veritabanına kullanıcının gönderdiği ham
    // casing ile yazılıyordu. Aynı mantıksal kategori farklı satırlarda
    // "Su"/"su"/"SU" olarak saklanabiliyordu; bu da HabitsController.GetHabits
    // içindeki case-sensitive `categoryFilter.Contains(h.Category)` SQL
    // filtresinin bazı kayıtları atlamasına yol açıyordu. Normalize, girdiyi
    // Allowed listesindeki KANONİK (doğru case'li) forma çevirir; geçersizse
    // null döner. Controller'lar artık DB'ye yazmadan önce bunu çağırıyor.
    public static string? Normalize(string? category)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return null;
        }

        return Allowed.FirstOrDefault(a => a.Equals(category, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsWater(string? category) => Matches(category, Water);

    public static bool IsReading(string? category) => Matches(category, Reading);

    public static bool IsFocus(string? category) => Matches(category, Focus);

    private static bool Matches(string? category, string target) =>
        !string.IsNullOrWhiteSpace(category) &&
        category.Equals(target, StringComparison.OrdinalIgnoreCase);
}