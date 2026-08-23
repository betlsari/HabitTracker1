using Models;
using Xunit;

namespace HabitTrackerApi.Tests;

public class HabitCategoriesTests
{
    [Theory]
    [InlineData("Su", true)]
    [InlineData("su", true)]
    [InlineData("Kitap", true)]
    [InlineData("Odaklanma", true)]
    [InlineData("Spor", true)]
    [InlineData("Diğer", true)]
    [InlineData("Water", false)] // DÜZELTİLDİ: alias artık kabul edilmiyor
    [InlineData("Su İçme", false)]
    [InlineData("RandomCategory", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValid_ChecksWhitelist(string? category, bool expected)
    {
        Assert.Equal(expected, HabitCategories.IsValid(category));
    }

    [Theory]
    [InlineData("Su", true)]
    [InlineData("Water", false)] 
    [InlineData("Su İçme", false)]
    public void IsWater_MatchesOnlyExactAllowedValues(string category, bool expected)
    {
        Assert.Equal(expected, HabitCategories.IsWater(category));
    }

    [Fact]
    public void Allowed_MatchesEverythingIsValidAccepts()
    {
        
        foreach (var category in HabitCategories.Allowed)
        {
            Assert.True(HabitCategories.IsValid(category));
        }
    }
}