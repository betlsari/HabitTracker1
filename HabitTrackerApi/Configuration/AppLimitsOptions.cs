namespace Configuration;

public sealed class AppLimitsOptions
{
    public const string SectionName = "AppLimits";

    public int MaxHabitsPerUser { get; init; } = 100;
    public int MaxBooksPerUser { get; init; } = 200;
    public int MaxPetsPerUser { get; init; } = 5;
    public int MaxDeviceTokensPerUser { get; init; } = 10;
}