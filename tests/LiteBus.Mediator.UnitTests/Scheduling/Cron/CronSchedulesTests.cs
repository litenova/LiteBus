using System;
using LiteBus.Commands.Recurring.Schedules.Cron;
using LiteBus.Runtime.Abstractions.Scheduling;

namespace LiteBus.Mediator.UnitTests.Scheduling.Cron;

/// <summary>
///     Covers the cron schedules: occurrences, zones, second precision, daylight-saving resolution, and expression
///     validation.
/// </summary>
public sealed class CronSchedulesTests
{
    /// <summary>
    ///     Verifies that a cron occurrence later the same day is returned for that day.
    /// </summary>
    [Fact]
    public void FromExpression_WhenOccurrenceIsAhead_ShouldReturnSameDay()
    {
        ISchedule schedule = CronSchedules.FromExpression("0 9 * * *");
        var after = new DateTimeOffset(2026, 1, 5, 8, 0, 0, TimeSpan.Zero);

        schedule.GetNextOccurrence(after).Should().Be(new DateTimeOffset(2026, 1, 5, 9, 0, 0, TimeSpan.Zero));
    }

    /// <summary>
    ///     Verifies that a cron occurrence already passed today is returned for the next day.
    /// </summary>
    [Fact]
    public void FromExpression_WhenOccurrenceHasPassed_ShouldReturnNextDay()
    {
        ISchedule schedule = CronSchedules.FromExpression("0 9 * * *");
        var after = new DateTimeOffset(2026, 1, 5, 10, 0, 0, TimeSpan.Zero);

        schedule.GetNextOccurrence(after).Should().Be(new DateTimeOffset(2026, 1, 6, 9, 0, 0, TimeSpan.Zero));
    }

    /// <summary>
    ///     Verifies that an occurrence exactly at the supplied instant is excluded and the following one is returned.
    /// </summary>
    [Fact]
    public void FromExpression_WhenAfterIsExactlyAtOccurrence_ShouldReturnFollowingOccurrence()
    {
        ISchedule schedule = CronSchedules.FromExpression("0 9 * * *");
        var after = new DateTimeOffset(2026, 1, 5, 9, 0, 0, TimeSpan.Zero);

        schedule.GetNextOccurrence(after).Should().Be(new DateTimeOffset(2026, 1, 6, 9, 0, 0, TimeSpan.Zero));
    }

    /// <summary>
    ///     Verifies that an omitted zone reads the expression as UTC.
    /// </summary>
    [Fact]
    public void FromExpression_WhenZoneIsOmitted_ShouldReadExpressionAsUtc()
    {
        ISchedule schedule = CronSchedules.FromExpression("0 9 * * *");

        // 10:00 at +02:00 is 08:00 UTC, so the 09:00 UTC occurrence is still ahead the same day.
        var after = new DateTimeOffset(2026, 1, 5, 10, 0, 0, TimeSpan.FromHours(2));

        schedule.GetNextOccurrence(after).Should().Be(new DateTimeOffset(2026, 1, 5, 9, 0, 0, TimeSpan.Zero));
    }

    /// <summary>
    ///     Verifies that the expression is read in the supplied zone.
    /// </summary>
    [Fact]
    public void FromExpression_WhenZoneIsFixedOffset_ShouldReadExpressionInZone()
    {
        ISchedule schedule = CronSchedules.FromExpression(
            "0 9 * * *",
            CreateFixedZone("CronSchedulesTests-Eastern", TimeSpan.FromHours(2)));
        var after = new DateTimeOffset(2026, 1, 5, 6, 0, 0, TimeSpan.Zero);

        // 09:00 at +02:00 is 07:00 UTC.
        schedule.GetNextOccurrence(after).Should().Be(new DateTimeOffset(2026, 1, 5, 7, 0, 0, TimeSpan.Zero));
    }

    /// <summary>
    ///     Verifies that a weekday expression returns the coming weekday.
    /// </summary>
    [Fact]
    public void FromExpression_WhenWeekdayIsComing_ShouldReturnComingWeekday()
    {
        ISchedule schedule = CronSchedules.FromExpression("0 9 * * 3");
        var after = new DateTimeOffset(2026, 1, 5, 8, 0, 0, TimeSpan.Zero);

        schedule.GetNextOccurrence(after).Should().Be(new DateTimeOffset(2026, 1, 7, 9, 0, 0, TimeSpan.Zero));
    }

    /// <summary>
    ///     Verifies that a six-field expression returns second-precision occurrences.
    /// </summary>
    [Fact]
    public void FromExpression_WhenFormatIncludesSeconds_ShouldReturnSecondPrecisionOccurrence()
    {
        ISchedule schedule = CronSchedules.FromExpression("30 * * * * *", CronExpressionFormat.IncludeSeconds);
        var after = new DateTimeOffset(2026, 1, 5, 8, 0, 0, TimeSpan.Zero);

        schedule.GetNextOccurrence(after).Should().Be(new DateTimeOffset(2026, 1, 5, 8, 0, 30, TimeSpan.Zero));
    }

    /// <summary>
    ///     Verifies that a field count that does not match the format is rejected.
    /// </summary>
    /// <param name="expression">The cron expression under test.</param>
    /// <param name="format">The format the expression is parsed with.</param>
    [Theory]
    [InlineData("30 * * * * *", CronExpressionFormat.Standard)]
    [InlineData("0 9 * * *", CronExpressionFormat.IncludeSeconds)]
    public void FromExpression_WhenFieldCountDoesNotMatchFormat_ShouldThrow(string expression, CronExpressionFormat format)
    {
        var act = () => CronSchedules.FromExpression(expression, format);

        act.Should().Throw<FormatException>();
    }

    /// <summary>
    ///     Verifies that a cron occurrence inside a daylight-saving gap resolves forward to the next valid instant.
    /// </summary>
    [Fact]
    public void FromExpression_WhenOccurrenceFallsInDaylightSavingGap_ShouldResolveToNextValidInstant()
    {
        ISchedule schedule = CronSchedules.FromExpression("30 2 * * *", CreateDaylightZone());

        // 2026-03-29 is the last Sunday of March 2026: clocks jump from 02:00 to 03:00.
        var after = new DateTimeOffset(2026, 3, 29, 0, 30, 0, TimeSpan.FromHours(1));

        var next = schedule.GetNextOccurrence(after);

        next.Should().Be(new DateTimeOffset(2026, 3, 29, 3, 0, 0, TimeSpan.FromHours(2)));
        next.Value.Offset.Should().Be(TimeSpan.FromHours(2));
    }

    /// <summary>
    ///     Verifies that a cron occurrence inside a fall-back overlap resolves to the earlier occurrence.
    /// </summary>
    [Fact]
    public void FromExpression_WhenOccurrenceIsAmbiguous_ShouldResolveToEarlierOccurrence()
    {
        ISchedule schedule = CronSchedules.FromExpression("30 2 * * *", CreateDaylightZone());

        // 2026-10-25 is the last Sunday of October 2026: clocks fall back from 03:00 to 02:00.
        var after = new DateTimeOffset(2026, 10, 25, 0, 30, 0, TimeSpan.FromHours(2));

        var next = schedule.GetNextOccurrence(after);

        next.Should().Be(new DateTimeOffset(2026, 10, 25, 2, 30, 0, TimeSpan.FromHours(2)));
        next.Value.Offset.Should().Be(TimeSpan.FromHours(2));
    }

    /// <summary>
    ///     Verifies that an interval expression fires on both sides of the repeated fall-back hour.
    /// </summary>
    [Fact]
    public void FromExpression_WhenIntervalExpressionCrossesFallBack_ShouldFireOnBothSidesOfRepeatedHour()
    {
        ISchedule schedule = CronSchedules.FromExpression("0 * * * *", CreateDaylightZone());

        // 2026-10-25 is the last Sunday of October 2026: clocks fall back from 03:00 to 02:00.
        var after = new DateTimeOffset(2026, 10, 25, 1, 30, 0, TimeSpan.FromHours(2));

        var first = schedule.GetNextOccurrence(after);

        first.Should().Be(new DateTimeOffset(2026, 10, 25, 2, 0, 0, TimeSpan.FromHours(2)));
        first.Value.Offset.Should().Be(TimeSpan.FromHours(2));

        var second = schedule.GetNextOccurrence(first.Value);

        second.Should().Be(new DateTimeOffset(2026, 10, 25, 2, 0, 0, TimeSpan.FromHours(1)));
        second.Value.Offset.Should().Be(TimeSpan.FromHours(1));
    }

    /// <summary>
    ///     Verifies that an expression with no reachable occurrence returns no next occurrence.
    /// </summary>
    [Fact]
    public void FromExpression_WhenExpressionHasNoOccurrence_ShouldReturnNull()
    {
        ISchedule schedule = CronSchedules.FromExpression("0 0 30 2 *");
        var after = new DateTimeOffset(2026, 1, 5, 8, 0, 0, TimeSpan.Zero);

        schedule.GetNextOccurrence(after).Should().BeNull();
    }

    /// <summary>
    ///     Verifies that an invalid expression is rejected when the schedule is created.
    /// </summary>
    /// <param name="expression">The invalid cron expression under test.</param>
    [Theory]
    [InlineData("not a cron")]
    [InlineData("61 * * * *")]
    [InlineData("* *")]
    public void FromExpression_WhenExpressionIsInvalid_ShouldThrow(string expression)
    {
        var act = () => CronSchedules.FromExpression(expression);

        act.Should().Throw<FormatException>();
    }

    /// <summary>
    ///     Verifies that a blank expression is rejected when the schedule is created.
    /// </summary>
    /// <param name="expression">The blank cron expression under test.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void FromExpression_WhenExpressionIsBlank_ShouldThrow(string? expression)
    {
        var act = () => CronSchedules.FromExpression(expression!);

        act.Should().Throw<ArgumentException>();
    }

    /// <summary>
    ///     Verifies that an undefined format is rejected when the schedule is created.
    /// </summary>
    [Fact]
    public void FromExpression_WhenFormatIsUndefined_ShouldThrow()
    {
        var act = () => CronSchedules.FromExpression("0 9 * * *", (CronExpressionFormat) 42);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>
    ///     Builds a zone with a fixed offset and no daylight-saving rules.
    /// </summary>
    /// <param name="id">The zone identifier.</param>
    /// <param name="offset">The fixed offset from UTC.</param>
    /// <returns>The zone.</returns>
    private static TimeZoneInfo CreateFixedZone(string id, TimeSpan offset)
    {
        return TimeZoneInfo.CreateCustomTimeZone(id, offset, id, id);
    }

    /// <summary>
    ///     Builds a zone at one hour east with spring-forward and fall-back transitions on the last Sundays of March
    ///     and October.
    /// </summary>
    /// <returns>The zone.</returns>
    private static TimeZoneInfo CreateDaylightZone()
    {
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            DateTime.MinValue.Date,
            DateTime.MaxValue.Date,
            TimeSpan.FromHours(1),
            TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 3, 5, DayOfWeek.Sunday),
            TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 3, 0, 0), 10, 5, DayOfWeek.Sunday));

        return TimeZoneInfo.CreateCustomTimeZone(
            "CronSchedulesTests-Daylight",
            TimeSpan.FromHours(1),
            "Schedule test zone",
            "Test standard time",
            "Test daylight time",
            [rule]);
    }
}
