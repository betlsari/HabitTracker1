using Models;

namespace Services;

public static class HabitSchedule
{
    public static DateTime PeriodStartLocal(DateTime utcInstant, HabitPeriod period, TimeZoneInfo tz)
    {
        var localDate = TimeZones.ToLocal(utcInstant, tz).Date;
        return period switch
        {
            HabitPeriod.Weekly => StartOfWeekMonday(localDate),
            HabitPeriod.Monthly => new DateTime(localDate.Year, localDate.Month, 1),
            _ => localDate
        };
    }

    public static DateTime NextPeriodStartLocal(DateTime periodStartLocal, HabitPeriod period)
    {
        return period switch
        {
            HabitPeriod.Weekly => periodStartLocal.AddDays(7),
            HabitPeriod.Monthly => periodStartLocal.AddMonths(1),
            _ => periodStartLocal.AddDays(1)
        };
    }

    public static DateTime PreviousPeriodStartLocal(DateTime periodStartLocal, HabitPeriod period)
    {
        return period switch
        {
            HabitPeriod.Weekly => periodStartLocal.AddDays(-7),
            HabitPeriod.Monthly => periodStartLocal.AddMonths(-1),
            _ => periodStartLocal.AddDays(-1)
        };
    }

    public static (DateTime UtcStart, DateTime UtcEndExclusive) UtcRange(
        DateTime periodStartLocal,
        HabitPeriod period,
        TimeZoneInfo tz)
    {
        var endLocal = NextPeriodStartLocal(periodStartLocal, period);
        return (TimeZones.ToUtc(periodStartLocal, tz), TimeZones.ToUtc(endLocal, tz));
    }

    public static DateTime PeriodStartLocalOfCompletion(DateTime completionUtc, HabitPeriod period, TimeZoneInfo tz)
    {
        return PeriodStartLocal(completionUtc, period, tz);
    }

    public static bool IsEndOfPeriodWindow(DateTime utcNow, HabitPeriod period, TimeZoneInfo tz, int localHour, int minuteTolerance)
    {
        var local = TimeZones.ToLocal(utcNow, tz);
        if (Math.Abs((local.TimeOfDay - TimeSpan.FromHours(localHour)).TotalMinutes) > minuteTolerance)
        {
            return false;
        }

        var periodStart = PeriodStartLocal(utcNow, period, tz);
        var periodEnd = NextPeriodStartLocal(periodStart, period);
        var lastDay = periodEnd.AddDays(-1).Date;
        return local.Date == lastDay;
    }

    
    public static bool IsWithinTargetTime(Habit habit, DateTime completionUtc, TimeZoneInfo tz)
    {
        if (!habit.TargetTime.HasValue)
        {
            return false;
        }

        var local = TimeZones.ToLocal(completionUtc, tz);
        return TimeOnly.FromDateTime(local) <= habit.TargetTime.Value;
    }

    private static DateTime StartOfWeekMonday(DateTime date)
    {
        var diff = ((int)date.DayOfWeek + 6) % 7;
        return date.AddDays(-diff);
    }
}