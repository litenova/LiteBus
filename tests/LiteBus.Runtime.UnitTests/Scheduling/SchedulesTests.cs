using System;
using LiteBus.Runtime.Scheduling;

namespace LiteBus.Runtime.UnitTests.Scheduling;

/// <summary>
///     Covers the shared schedules: interval, daily, and weekly occurrences and daylight-saving resolution.
/// </summary>
public sealed class SchedulesTests
{
    [Fact]
    public void Every_WhenIntervalIsPositive_ShouldReturnInstantOneIntervalLater()
    {
        var schedule = Schedules.Every(TimeSpan.FromMinutes(30));
        var after = new DateTimeOffset(2026, 1, 5, 8, 0, 0, TimeSpan.Zero);

        schedule.GetNextOccurrence(after).Should().Be(after.Add(TimeSpan.FromMinutes(30)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Every_WhenIntervalIsNotPositive_ShouldThrow(int minutes)
    {
        var act = () => Schedules.Every(TimeSpan.FromMinutes(minutes));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void DailyAt_WhenTimeIsAhead_ShouldReturnToday()
    {
        var schedule = Schedules.DailyAt(new TimeOnly(9, 0));
        var after = new DateTimeOffset(2026, 1, 5, 8, 0, 0, TimeSpan.Zero);

        schedule.GetNextOccurrence(after).Should().Be(new DateTimeOffset(2026, 1, 5, 9, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void DailyAt_WhenTimeHasPassed_ShouldReturnTomorrow()
    {
        var schedule = Schedules.DailyAt(new TimeOnly(9, 0));
        var after = new DateTimeOffset(2026, 1, 5, 10, 0, 0, TimeSpan.Zero);

        schedule.GetNextOccurrence(after).Should().Be(new DateTimeOffset(2026, 1, 6, 9, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void DailyAt_WhenTimeIsNow_ShouldReturnTomorrow()
    {
        var schedule = Schedules.DailyAt(new TimeOnly(9, 0));
        var after = new DateTimeOffset(2026, 1, 5, 9, 0, 0, TimeSpan.Zero);

        schedule.GetNextOccurrence(after).Should().Be(new DateTimeOffset(2026, 1, 6, 9, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void DailyAt_WhenZoneIsOmitted_ShouldReadTimeAsUtc()
    {
        var schedule = Schedules.DailyAt(new TimeOnly(9, 0));

        // 10:00 at +02:00 is 08:00 UTC, so the 09:00 UTC occurrence is still ahead the same day.
        var after = new DateTimeOffset(2026, 1, 5, 10, 0, 0, TimeSpan.FromHours(2));

        schedule.GetNextOccurrence(after).Should().Be(new DateTimeOffset(2026, 1, 5, 9, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void WeeklyOn_WhenWeekdayAndTimeAreAhead_ShouldReturnSameDay()
    {
        var schedule = Schedules.WeeklyOn(DayOfWeek.Monday, new TimeOnly(9, 0));
        var after = new DateTimeOffset(2026, 1, 5, 8, 0, 0, TimeSpan.Zero);

        schedule.GetNextOccurrence(after).Should().Be(new DateTimeOffset(2026, 1, 5, 9, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void WeeklyOn_WhenTimeHasPassed_ShouldReturnNextWeek()
    {
        var schedule = Schedules.WeeklyOn(DayOfWeek.Monday, new TimeOnly(9, 0));
        var after = new DateTimeOffset(2026, 1, 5, 10, 0, 0, TimeSpan.Zero);

        schedule.GetNextOccurrence(after).Should().Be(new DateTimeOffset(2026, 1, 12, 9, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void WeeklyOn_WhenWeekdayIsComing_ShouldReturnComingWeekday()
    {
        var schedule = Schedules.WeeklyOn(DayOfWeek.Wednesday, new TimeOnly(9, 0));
        var after = new DateTimeOffset(2026, 1, 5, 8, 0, 0, TimeSpan.Zero);

        schedule.GetNextOccurrence(after).Should().Be(new DateTimeOffset(2026, 1, 7, 9, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void WeeklyOn_WhenWeekdayIsUndefined_ShouldThrow()
    {
        var act = () => Schedules.WeeklyOn((DayOfWeek) 42, new TimeOnly(9, 0));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void DailyAt_WhenTimeFallsInDaylightSavingGap_ShouldResolveToNextValidInstant()
    {
        var schedule = Schedules.DailyAt(new TimeOnly(2, 30), CreateDaylightZone());

        // 2026-03-29 is the last Sunday of March 2026: clocks jump from 02:00 to 03:00.
        var after = new DateTimeOffset(2026, 3, 29, 0, 30, 0, TimeSpan.FromHours(1));

        schedule.GetNextOccurrence(after).Should().Be(new DateTimeOffset(2026, 3, 29, 3, 0, 0, TimeSpan.FromHours(2)));
    }

    [Fact]
    public void DailyAt_WhenTimeIsAmbiguous_ShouldResolveToEarlierOccurrence()
    {
        var schedule = Schedules.DailyAt(new TimeOnly(2, 30), CreateDaylightZone());

        // 2026-10-25 is the last Sunday of October 2026: clocks fall back from 03:00 to 02:00.
        var after = new DateTimeOffset(2026, 10, 25, 0, 30, 0, TimeSpan.FromHours(2));

        schedule.GetNextOccurrence(after).Should().Be(new DateTimeOffset(2026, 10, 25, 2, 30, 0, TimeSpan.FromHours(2)));
    }

    [Fact]
    public void WeeklyOn_WhenTimeFallsInDaylightSavingGap_ShouldResolveToNextValidInstant()
    {
        var schedule = Schedules.WeeklyOn(DayOfWeek.Sunday, new TimeOnly(2, 30), CreateDaylightZone());
        var after = new DateTimeOffset(2026, 3, 28, 12, 0, 0, TimeSpan.FromHours(1));

        schedule.GetNextOccurrence(after).Should().Be(new DateTimeOffset(2026, 3, 29, 3, 0, 0, TimeSpan.FromHours(2)));
    }

    private static TimeZoneInfo CreateDaylightZone()
    {
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            DateTime.MinValue.Date,
            DateTime.MaxValue.Date,
            TimeSpan.FromHours(1),
            TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 3, 5, DayOfWeek.Sunday),
            TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 3, 0, 0), 10, 5, DayOfWeek.Sunday));

        return TimeZoneInfo.CreateCustomTimeZone(
            "SchedulesTests-Daylight",
            TimeSpan.FromHours(1),
            "Schedule test zone",
            "Test standard time",
            "Test daylight time",
            [rule]);
    }
}
