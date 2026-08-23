namespace Configuration;

public sealed class AppLimitsOptions
{
    public const string SectionName = "AppLimits";

    public int MaxHabitsPerUser { get; init; } = 100;
    public int MaxBooksPerUser { get; init; } = 200;
    public int MaxPetsPerUser { get; init; } = 5;
    public int MaxDeviceTokensPerUser { get; init; } = 10;

    public int PetEggCostXp { get; init; } = 50;
    public int PetFeedCostXp { get; init; } = 3;
    public int PetFeedXpGain { get; init; } = 20;

    
    public int MaxPetLevel { get; init; } = 100;
}