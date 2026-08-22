namespace Models;


public static class PetTypes
{
    public static readonly string[] Allowed =
    {
        "Cat",
        "Dog",
        "Panda",
        "Rabbit"
    };

    public static bool IsValid(string type) =>
        !string.IsNullOrWhiteSpace(type) &&
        Allowed.Contains(type, StringComparer.OrdinalIgnoreCase);
}
