using Models;
using Services;
using Xunit;

namespace HabitTrackerApi.Tests;

public class HabitScheduleTests
{
    private static readonly TimeZoneInfo Istanbul = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");

    [Fact]
    public void PeriodStartLocal_Daily_ReturnsDateOnly()
    {
        var utc = new DateTime(2026, 8, 22, 10, 30, 0, DateTimeKind.Utc);

        var result = HabitSchedule.PeriodStartLocal(utc, HabitPeriod.Daily, Istanbul);

        Assert.Equal(new DateTime(2026, 8, 22), result.Date);
        Assert.Equal(TimeSpan.Zero, result.TimeOfDay);
    }

    [Fact]
    public void PeriodStartLocal_Weekly_ReturnsMonday()
    {
        // 22 Ağustos 2026 bir Cumartesi'dir; haftanın başlangıcı 17 Ağustos Pazartesi olmalı.
        var utc = new DateTime(2026, 8, 22, 10, 0, 0, DateTimeKind.Utc);

        var result = HabitSchedule.PeriodStartLocal(utc, HabitPeriod.Weekly, Istanbul);

        Assert.Equal(DayOfWeek.Monday, result.DayOfWeek);
        Assert.True(result.Date <= new DateTime(2026, 8, 22));
    }

    [Fact]
    public void PeriodStartLocal_Monthly_ReturnsFirstOfMonth()
    {
        var utc = new DateTime(2026, 8, 22, 10, 0, 0, DateTimeKind.Utc);

        var result = HabitSchedule.PeriodStartLocal(utc, HabitPeriod.Monthly, Istanbul);

        Assert.Equal(1, result.Day);
        Assert.Equal(8, result.Month);
    }

    [Theory]
    [InlineData(HabitPeriod.Daily, 1)]
    [InlineData(HabitPeriod.Weekly, 7)]
    public void NextPeriodStartLocal_AddsCorrectAmountOfDays(HabitPeriod period, int expectedDays)
    {
        var start = new DateTime(2026, 8, 17);

        var next = HabitSchedule.NextPeriodStartLocal(start, period);

        Assert.Equal(start.AddDays(expectedDays), next);
    }

    [Fact]
    public void NextPeriodStartLocal_Monthly_AddsOneMonth()
    {
        var start = new DateTime(2026, 8, 1);

        var next = HabitSchedule.NextPeriodStartLocal(start, HabitPeriod.Monthly);

        Assert.Equal(new DateTime(2026, 9, 1), next);
    }

    [Fact]
    public void PreviousPeriodStartLocal_IsInverseOfNext()
    {
        var start = new DateTime(2026, 8, 17);
        var next = HabitSchedule.NextPeriodStartLocal(start, HabitPeriod.Weekly);

        var previous = HabitSchedule.PreviousPeriodStartLocal(next, HabitPeriod.Weekly);

        Assert.Equal(start, previous);
    }
}