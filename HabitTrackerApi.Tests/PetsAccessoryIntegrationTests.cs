using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Xunit;

namespace HabitTrackerApi.Tests;

public sealed class PetsAccessoryIntegrationTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public PetsAccessoryIntegrationTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task GetAccessories_ForNewPet_AllLocked()
    {
        using var client = _factory.CreateClient();
        var token = await _factory.CreateAccessTokenAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var createPet = await client.PostAsJsonAsync("/api/pets", new { type = "Cat" });
        createPet.EnsureSuccessStatusCode();
        var pet = await createPet.Content.ReadFromJsonAsync<PetResponse>();

        var response = await client.GetAsync($"/api/pets/{pet!.Id}/accessories");

        response.EnsureSuccessStatusCode();
        var accessories = await response.Content.ReadFromJsonAsync<List<AccessoryResponse>>();
        Assert.All(accessories!, a => Assert.False(a.Unlocked));
    }

    [Fact]
    public async Task EquipAccessory_WhenNotUnlocked_ReturnsBadRequest()
    {
        using var client = _factory.CreateClient();
        var token = await _factory.CreateAccessTokenAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var createPet = await client.PostAsJsonAsync("/api/pets", new { type = "Dog" });
        createPet.EnsureSuccessStatusCode();
        var pet = await createPet.Content.ReadFromJsonAsync<PetResponse>();

        var response = await client.PutAsJsonAsync($"/api/pets/{pet!.Id}/accessory", new { accessory = "Hat" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task EquipAccessory_WithNullAccessory_UnequipsSuccessfully()
    {
        using var client = _factory.CreateClient();
        var token = await _factory.CreateAccessTokenAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var createPet = await client.PostAsJsonAsync("/api/pets", new { type = "Panda" });
        createPet.EnsureSuccessStatusCode();
        var pet = await createPet.Content.ReadFromJsonAsync<PetResponse>();

        var response = await client.PutAsJsonAsync($"/api/pets/{pet!.Id}/accessory", new { accessory = (string?)null });

        response.EnsureSuccessStatusCode();
        var updated = await response.Content.ReadFromJsonAsync<PetResponse>();
        Assert.Null(updated!.EquippedAccessory);
    }

    private sealed class PetResponse
    {
        public int Id { get; set; }
        public string? EquippedAccessory { get; set; }
    }

    private sealed class AccessoryResponse
    {
        public string Code { get; set; } = string.Empty;
        public bool Unlocked { get; set; }
        public bool Equipped { get; set; }
    }
}