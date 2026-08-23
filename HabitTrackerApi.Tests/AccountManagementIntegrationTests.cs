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

        var token =
            await _factory.CreateAccessTokenAsync();

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                token);

        var response =
            await client.PostAsJsonAsync(
                "/api/auth/change-password",
                new
                {
                    currentPassword = "WrongPassword1A",
                    newPassword = "NewIntegration1A"
                });

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
    }


    [Fact]
    public async Task ChangePassword_WithCorrectPassword_RevokesExistingSessions()
    {
        using var client = _factory.CreateClient();

        // Email ve eski access token oluştur.
        var (email, oldToken) =
            await _factory.CreateAccessTokenWithEmailAsync();

        // Eski token ile authenticated ol.
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                oldToken);


        // ========================================================
        // 1. Şifreyi değiştir
        // ========================================================

        var changeResponse =
            await client.PostAsJsonAsync(
                "/api/auth/change-password",
                new
                {
                    currentPassword = "Integration1A",
                    newPassword = "NewIntegration1A"
                });

        changeResponse.EnsureSuccessStatusCode();


        // ========================================================
        // 2. Eski token artık geçersiz olmalı
        // ========================================================

        var oldTokenCheck =
            await client.GetAsync("/api/auth/me");

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            oldTokenCheck.StatusCode);


        // ========================================================
        // 3. Yeni şifre ile login
        // ========================================================

        var loginResponse =
            await client.PostAsJsonAsync(
                "/api/auth/login",
                new
                {
                    email,
                    password = "NewIntegration1A"
                });

        loginResponse.EnsureSuccessStatusCode();


        var login =
            await loginResponse.Content
                .ReadFromJsonAsync<LoginResponse>();

        Assert.NotNull(login);

        Assert.False(
            string.IsNullOrWhiteSpace(login!.Token));


        // ========================================================
        // 4. Yeni token'ı kullan
        // ========================================================

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                login.Token);


        // ========================================================
        // 5. Eski session'ların iptal edildiğini kontrol et
        // ========================================================

        var sessions =
            await client.GetAsync(
                "/api/auth/sessions");

        sessions.EnsureSuccessStatusCode();


        var list = await sessions.Content.ReadFromJsonAsync<List<object>>();

Assert.NotNull(list);
Assert.Single(list);

        
    }


    // ============================================================
    // DELETE ACCOUNT
    // ============================================================

    [Fact]
    public async Task DeleteAccount_WithWrongPassword_ReturnsBadRequest()
    {
        using var client = _factory.CreateClient();

        var token =
            await _factory.CreateAccessTokenAsync();

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                token);


        var request =
            new HttpRequestMessage(
                HttpMethod.Delete,
                "/api/auth/me")
            {
                Content = JsonContent.Create(
                    new
                    {
                        currentPassword =
                            "WrongPassword1A"
                    })
            };


        var response =
            await client.SendAsync(request);


        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
    }


    [Fact]
    public async Task DeleteAccount_WithCorrectPassword_Succeeds()
    {
        using var client = _factory.CreateClient();

        var token =
            await _factory.CreateAccessTokenAsync();

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                token);


        var request =
            new HttpRequestMessage(
                HttpMethod.Delete,
                "/api/auth/me")
            {
                Content = JsonContent.Create(
                    new
                    {
                        currentPassword =
                            "Integration1A"
                    })
            };


        var response =
            await client.SendAsync(request);


        response.EnsureSuccessStatusCode();


        // Hesap silindikten sonra /me endpoint'ine
        // erişilememeli.
        var meResponse =
            await client.GetAsync(
                "/api/auth/me");


        Assert.Equal(
            HttpStatusCode.Unauthorized,
            meResponse.StatusCode);
    }


    // ============================================================
    // LOGIN RESPONSE
    // ============================================================

    private sealed class LoginResponse
    {
        public bool RequiresTwoFactor { get; set; }

        public string? Token { get; set; }

        public string? RefreshToken { get; set; }
    }
}