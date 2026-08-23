namespace Models;

public static class PetAccessories
{
    public const string Hat = "Hat";
    public const string Glasses = "Glasses";
    public const string Bowtie = "Bowtie";

    public static readonly string[] Allowed = { Hat, Glasses, Bowtie };

    public static bool IsValid(string? accessory) =>
        !string.IsNullOrWhiteSpace(accessory) &&
        Allowed.Contains(accessory, StringComparer.OrdinalIgnoreCase);
}