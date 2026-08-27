namespace Models;

public static class AuthAuditEventTypes
{
    public const string Register = "Register";
    public const string LoginSucceeded = "LoginSucceeded";
    public const string LoginFailed = "LoginFailed";
    public const string LoginLockedOut = "LoginLockedOut";
    public const string Logout = "Logout";
    public const string LogoutAll = "LogoutAll";
    public const string PasswordChanged = "PasswordChanged";
    public const string PasswordChangeFailed = "PasswordChangeFailed";
    public const string ForgotPasswordRequested = "ForgotPasswordRequested";
    public const string PasswordReset = "PasswordReset";
    public const string PasswordResetFailed = "PasswordResetFailed";
    public const string EmailConfirmed = "EmailConfirmed";
    public const string EmailConfirmationFailed = "EmailConfirmationFailed";
    public const string RefreshTokenReused = "RefreshTokenReused"; // olası token hırsızlığı sinyali
    public const string AccountDeleted = "AccountDeleted";
    public const string AccountDeleteFailed = "AccountDeleteFailed";
}

public class AuthAuditEvent
{
    public long Id { get; set; }

    
    public string? UserId { get; set; }

    public required string Email { get; set; }

    public required string EventType { get; set; }

    public bool Succeeded { get; set; }

    public string? IpAddress { get; set; }

    public string? UserAgent { get; set; }

    // Ek bağlam (örn. "Invalid password", "Account locked out" gibi kısa açıklama).
    // Asla şifre veya token gibi hassas veri buraya yazılmamalı.
    public string? Detail { get; set; }

    public DateTime CreatedAt { get; set; }
}