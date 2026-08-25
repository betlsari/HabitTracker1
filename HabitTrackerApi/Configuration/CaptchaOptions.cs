namespace Configuration;

public sealed class CaptchaOptions
{
    public const string SectionName = "Captcha";

    public bool Enabled { get; set; }

    public string Provider { get; set; } = "Turnstile";

    public string Secret { get; set; } = string.Empty;

    public string HeaderName { get; set; } = "X-Captcha-Token";

    public string FormFieldName { get; set; } = "captchaToken";

    public string VerifyUrl { get; set; } = string.Empty;
}
