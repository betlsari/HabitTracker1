using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

using Xunit;

namespace HabitTrackerApi.Tests;

public sealed class AccountManagementIntegrationTests
    : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public AccountManagementIntegrationTests(ApiFactory factory)
    {
        _factory = factory;
    }

    // ============================================================
    // CHANGE PASSWORD
    // ============================================================

    [Fact]
    public async Task ChangePassword_WithWrongCurrentPassword_ReturnsBadRequest()
    {
        using var client = _factory.CreateClient();
        var token = await _factory.CreateAccessTokenAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync(
            "/api/auth/change-password",
            new { currentPassword = "WrongPassword1A", newPassword = "NewIntegration1A" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_WithCorrectPassword_RevokesExistingSessions()
    {
        using var client = _factory.CreateClient();

        var (email, oldToken) = await _factory.CreateAccessTokenWithEmailAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", oldToken);

        var changeResponse = await client.PostAsJsonAsync(
            "/api/auth/change-password",
            new { currentPassword = "Integration1A", newPassword = "NewIntegration1A" });
        changeResponse.EnsureSuccessStatusCode();

        var oldTokenCheck = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, oldTokenCheck.StatusCode);

        var loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { email, password = "NewIntegration1A" });
        loginResponse.EnsureSuccessStatusCode();

        var login = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(login);
        Assert.False(string.IsNullOrWhiteSpace(login!.Token));
    }

    // ============================================================
    // DELETE ACCOUNT
    // ============================================================

    [Fact]
    public async Task DeleteAccount_WithWrongPassword_ReturnsBadRequest()
    {
        using var client = _factory.CreateClient();
        var token = await _factory.CreateAccessTokenAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var request = new HttpRequestMessage(HttpMethod.Delete, "/api/auth/me")
        {
            Content = JsonContent.Create(new { currentPassword = "WrongPassword1A" })
        };

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DeleteAccount_WithCorrectPassword_Succeeds()
    {
        using var client = _factory.CreateClient();
        var token = await _factory.CreateAccessTokenAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var request = new HttpRequestMessage(HttpMethod.Delete, "/api/auth/me")
        {
            Content = JsonContent.Create(new { currentPassword = "Integration1A" })
        };

        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var meResponse = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, meResponse.StatusCode);
    }

    private sealed class LoginResponse
    {
        public bool RequiresTwoFactor { get; set; }
        public string? Token { get; set; }
        public string? RefreshToken { get; set; }
    }
}