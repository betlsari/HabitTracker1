namespace Models;

public static class PetBackgrounds
{
    public const string Home = "Home";
    public const string Forest = "Forest";
    public const string Beach = "Beach";

    public static readonly string[] Allowed = { Home, Forest, Beach };

    public static bool IsValid(string? background) =>
        !string.IsNullOrWhiteSpace(background) &&
        Allowed.Contains(background, StringComparer.OrdinalIgnoreCase);
}