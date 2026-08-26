namespace Configuration;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";
    public string Key { get; init; } = string.Empty;
    public string Issuer { get; init; } = string.Empty;
    public string Audience { get; init; } = string.Empty;

    public int AccessTokenLifetimeMinutes { get; init; } = 120;
    public int RefreshTokenLifetimeDays { get; init; } = 7;
}