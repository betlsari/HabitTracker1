namespace Models;

public static class HabitCategories
{
    public static bool IsWater(string? category) =>
        !string.IsNullOrWhiteSpace(category) &&
        (category.Equals("Su", StringComparison.OrdinalIgnoreCase) ||
         category.Equals("Water", StringComparison.OrdinalIgnoreCase));

    public static bool IsReading(string? category) =>
        !string.IsNullOrWhiteSpace(category) &&
        (category.Equals("Kitap", StringComparison.OrdinalIgnoreCase) ||
         category.Equals("Reading", StringComparison.OrdinalIgnoreCase) ||
         category.Equals("Okuma", StringComparison.OrdinalIgnoreCase));
}
