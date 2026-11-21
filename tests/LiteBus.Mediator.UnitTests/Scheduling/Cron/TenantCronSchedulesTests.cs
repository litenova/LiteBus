using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Recurring.Schedules.Cron;
using LiteBus.Runtime.Abstractions.Scheduling;

namespace LiteBus.Mediator.UnitTests.Scheduling.Cron;

/// <summary>
///     Covers the per-tenant cron schedules: per-tenant zones, unknown-zone fallback, daylight-saving resolution, and
///     expression validation.
/// </summary>
public sealed class TenantCronSchedulesTests
{
    /// <summary>
    ///     Verifies that each tenant's occurrence is read in that tenant's zone.
    /// </summary>
    [Fact]
    public async Task FromExpression_WhenTenantsHaveDifferentZones_ShouldReturnEachTenantLocalMidnight()
    {
        var eastern = CreateFixedZone("TenantCronSchedulesTests-Eastern", TimeSpan.FromHours(2));
        var western = CreateFixedZone("TenantCronSchedulesTests-Western", TimeSpan.FromHours(-5));
        ITenantSchedule schedule = CronSchedules.FromExpression(
            "0 0 * * *",
            (string tenantId, CancellationToken cancellationToken) => ValueTask.FromResult<TimeZoneInfo?>(
                tenantId == "acme" ? eastern : western));

        var after = new DateTimeOffset(2026, 1, 5, 12, 0, 0, TimeSpan.Zero);

        var acme = await schedule.GetNextOccurrenceAsync("acme", after, CancellationToken.None).ConfigureAwait(false);
        var globex = await schedule.GetNextOccurrenceAsync("globex", after, CancellationToken.None).ConfigureAwait(false);

        acme.Should().Be(new DateTimeOffset(2026, 1, 5, 22, 0, 0, TimeSpan.Zero));
        globex.Should().Be(new DateTimeOffset(2026, 1, 6, 5, 0, 0, TimeSpan.Zero));
    }

    /// <summary>
    ///     Verifies that a tenant whose zone is unknown reads the expression as UTC.
    /// </summary>
    [Fact]
    public async Task FromExpression_WhenZoneIsUnknown_ShouldFallBackToUtc()
    {
        ITenantSchedule schedule = CronSchedules.FromExpression(
            "0 9 * * *",
            (string tenantId, CancellationToken cancellationToken) => ValueTask.FromResult<TimeZoneInfo?>(null));

        var after = new DateTimeOffset(2026, 1, 5, 8, 0, 0, TimeSpan.Zero);

        var next = await schedule.GetNextOccurrenceAsync("acme", after, CancellationToken.None).ConfigureAwait(false);

        next.Should().Be(new DateTimeOffset(2026, 1, 5, 9, 0, 0, TimeSpan.Zero));
    }

    /// <summary>
    ///     Verifies that a tenant occurrence inside a daylight-saving gap resolves forward to the next valid instant.
    /// </summary>
    [Fact]
    public async Task FromExpression_WhenTenantTimeFallsInDaylightSavingGap_ShouldResolveToNextValidInstant()
    {
        var daylight = CreateDaylightZone();
        ITenantSchedule schedule = CronSchedules.FromExpression(
            "30 2 * * *",
            (string tenantId, CancellationToken cancellationToken) => ValueTask.FromResult<TimeZoneInfo?>(daylight));

        // 2026-03-29 is the last Sunday of March 2026: clocks jump from 02:00 to 03:00.
        var after = new DateTimeOffset(2026, 3, 29, 0, 30, 0, TimeSpan.FromHours(1));

        var next = await schedule.GetNextOccurrenceAsync("acme", after, CancellationToken.None).ConfigureAwait(false);

        next.Should().Be(new DateTimeOffset(2026, 3, 29, 3, 0, 0, TimeSpan.FromHours(2)));
    }

    /// <summary>
    ///     Verifies that the zone lookup receives the requested tenant identifier.
    /// </summary>
    [Fact]
    public async Task GetNextOccurrenceAsync_ShouldResolveZoneForRequestedTenant()
    {
        string? resolvedFor = null;
        ITenantSchedule schedule = CronSchedules.FromExpression(
            "0 9 * * *",
            (string tenantId, CancellationToken cancellationToken) =>
            {
                resolvedFor = tenantId;
                return ValueTask.FromResult<TimeZoneInfo?>(TimeZoneInfo.Utc);
            });

        await schedule
            .GetNextOccurrenceAsync("acme", new DateTimeOffset(2026, 1, 5, 8, 0, 0, TimeSpan.Zero), CancellationToken.None)
            .ConfigureAwait(false);

        resolvedFor.Should().Be("acme");
    }

    /// <summary>
    ///     Verifies that a cancelled zone lookup stops the occurrence computation.
    /// </summary>
    [Fact]
    public async Task GetNextOccurrenceAsync_WhenLookupIsCanceled_ShouldObserveCancellation()
    {
        ITenantSchedule schedule = CronSchedules.FromExpression(
            "0 9 * * *",
            (string tenantId, CancellationToken cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return ValueTask.FromResult<TimeZoneInfo?>(TimeZoneInfo.Utc);
            });

        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync().ConfigureAwait(false);

        var act = async () =>
        {
            await schedule
                .GetNextOccurrenceAsync("acme", new DateTimeOffset(2026, 1, 5, 8, 0, 0, TimeSpan.Zero), canceled.Token)
                .ConfigureAwait(false);
        };

        await act.Should().ThrowAsync<OperationCanceledException>().ConfigureAwait(false);
    }

    /// <summary>
    ///     Verifies that a blank tenant identifier is rejected.
    /// </summary>
    /// <param name="tenantId">The blank tenant identifier under test.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task GetNextOccurrenceAsync_WhenTenantIdIsBlank_ShouldThrow(string? tenantId)
    {
        ITenantSchedule schedule = CronSchedules.FromExpression(
            "0 9 * * *",
            (string tenant, CancellationToken cancellationToken) => ValueTask.FromResult<TimeZoneInfo?>(TimeZoneInfo.Utc));

        var act = async () =>
        {
            await schedule
                .GetNextOccurrenceAsync(tenantId!, new DateTimeOffset(2026, 1, 5, 8, 0, 0, TimeSpan.Zero), CancellationToken.None)
                .ConfigureAwait(false);
        };

        await act.Should().ThrowAsync<ArgumentException>().ConfigureAwait(false);
    }

    /// <summary>
    ///     Verifies that a missing zone lookup is rejected when the schedule is created.
    /// </summary>
    [Fact]
    public void FromExpression_WhenZoneOfIsNull_ShouldThrow()
    {
        var act = () => CronSchedules.FromExpression(
            "0 9 * * *",
            (Func<string, CancellationToken, ValueTask<TimeZoneInfo?>>) null!);

        act.Should().Throw<ArgumentNullException>();
    }

    /// <summary>
    ///     Verifies that a missing zone lookup is rejected when a schedule with an explicit format is created.
    /// </summary>
    [Fact]
    public void FromExpressionWithFormat_WhenZoneOfIsNull_ShouldThrow()
    {
        var act = () => CronSchedules.FromExpression(
            "0 9 * * *",
            CronExpressionFormat.Standard,
            (Func<string, CancellationToken, ValueTask<TimeZoneInfo?>>) null!);

        act.Should().Throw<ArgumentNullException>();
    }

    /// <summary>
    ///     Verifies that an undefined format is rejected when the schedule is created.
    /// </summary>
    [Fact]
    public void FromExpression_WhenFormatIsUndefined_ShouldThrow()
    {
        var act = () => CronSchedules.FromExpression(
            "0 9 * * *",
            (CronExpressionFormat) 42,
            (string tenantId, CancellationToken cancellationToken) => ValueTask.FromResult<TimeZoneInfo?>(TimeZoneInfo.Utc));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>
    ///     Verifies that an invalid expression is rejected when the schedule is created.
    /// </summary>
    [Fact]
    public void FromExpression_WhenExpressionIsInvalid_ShouldThrow()
    {
        var act = () => CronSchedules.FromExpression(
            "not a cron",
            (string tenantId, CancellationToken cancellationToken) => ValueTask.FromResult<TimeZoneInfo?>(TimeZoneInfo.Utc));

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
        var act = () => CronSchedules.FromExpression(
            expression!,
            (string tenantId, CancellationToken cancellationToken) => ValueTask.FromResult<TimeZoneInfo?>(TimeZoneInfo.Utc));

        act.Should().Throw<ArgumentException>();
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
            "TenantCronSchedulesTests-Daylight",
            TimeSpan.FromHours(1),
            "Schedule test zone",
            "Test standard time",
            "Test daylight time",
            [rule]);
    }
}
