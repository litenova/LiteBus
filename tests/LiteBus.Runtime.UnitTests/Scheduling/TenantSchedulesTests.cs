using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Runtime.Abstractions.Scheduling;
using LiteBus.Runtime.Scheduling;

namespace LiteBus.Runtime.UnitTests.Scheduling;

/// <summary>
///     Covers the per-tenant schedules: per-tenant zones, unknown-zone fallback, and daylight-saving resolution.
/// </summary>
public sealed class TenantSchedulesTests
{
    [Fact]
    public async Task DailyAt_WhenTenantsHaveDifferentZones_ShouldReturnEachTenantLocalMidnight()
    {
        var eastern = CreateFixedZone("TenantSchedulesTests-Eastern", TimeSpan.FromHours(2));
        var western = CreateFixedZone("TenantSchedulesTests-Western", TimeSpan.FromHours(-5));
        ITenantSchedule schedule = Schedules.DailyAt(
            new TimeOnly(0, 0),
            (string tenantId, CancellationToken cancellationToken) => ValueTask.FromResult<TimeZoneInfo?>(
                tenantId == "acme" ? eastern : western));

        var after = new DateTimeOffset(2026, 1, 5, 12, 0, 0, TimeSpan.Zero);

        var acme = await schedule.GetNextOccurrenceAsync("acme", after, CancellationToken.None).ConfigureAwait(false);
        var globex = await schedule.GetNextOccurrenceAsync("globex", after, CancellationToken.None).ConfigureAwait(false);

        acme.Should().Be(new DateTimeOffset(2026, 1, 5, 22, 0, 0, TimeSpan.Zero));
        globex.Should().Be(new DateTimeOffset(2026, 1, 6, 5, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task WeeklyOn_WhenTenantsHaveDifferentZones_ShouldHonorEachTenantZone()
    {
        var eastern = CreateFixedZone("TenantSchedulesTests-Eastern", TimeSpan.FromHours(2));
        ITenantSchedule schedule = Schedules.WeeklyOn(
            DayOfWeek.Monday,
            new TimeOnly(9, 0),
            (string tenantId, CancellationToken cancellationToken) => ValueTask.FromResult<TimeZoneInfo?>(
                tenantId == "acme" ? eastern : TimeZoneInfo.Utc));

        // 2026-01-05 is a Monday: 09:00 at +02:00 is 07:00 UTC, still ahead of 06:00 UTC.
        var after = new DateTimeOffset(2026, 1, 5, 6, 0, 0, TimeSpan.Zero);

        var acme = await schedule.GetNextOccurrenceAsync("acme", after, CancellationToken.None).ConfigureAwait(false);
        var other = await schedule.GetNextOccurrenceAsync("other", after, CancellationToken.None).ConfigureAwait(false);

        acme.Should().Be(new DateTimeOffset(2026, 1, 5, 7, 0, 0, TimeSpan.Zero));
        other.Should().Be(new DateTimeOffset(2026, 1, 5, 9, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task DailyAt_WhenZoneIsUnknown_ShouldFallBackToUtc()
    {
        ITenantSchedule schedule = Schedules.DailyAt(
            new TimeOnly(9, 0),
            (string tenantId, CancellationToken cancellationToken) => ValueTask.FromResult<TimeZoneInfo?>(null));

        var after = new DateTimeOffset(2026, 1, 5, 8, 0, 0, TimeSpan.Zero);

        var next = await schedule.GetNextOccurrenceAsync("acme", after, CancellationToken.None).ConfigureAwait(false);

        next.Should().Be(new DateTimeOffset(2026, 1, 5, 9, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task DailyAt_WhenTenantTimeFallsInDaylightSavingGap_ShouldResolveToNextValidInstant()
    {
        var daylight = CreateDaylightZone();
        ITenantSchedule schedule = Schedules.DailyAt(
            new TimeOnly(2, 30),
            (string tenantId, CancellationToken cancellationToken) => ValueTask.FromResult<TimeZoneInfo?>(daylight));

        // 2026-03-29 is the last Sunday of March 2026: clocks jump from 02:00 to 03:00.
        var after = new DateTimeOffset(2026, 3, 29, 0, 30, 0, TimeSpan.FromHours(1));

        var next = await schedule.GetNextOccurrenceAsync("acme", after, CancellationToken.None).ConfigureAwait(false);

        next.Should().Be(new DateTimeOffset(2026, 3, 29, 3, 0, 0, TimeSpan.FromHours(2)));
    }

    [Fact]
    public async Task GetNextOccurrenceAsync_ShouldResolveZoneForRequestedTenant()
    {
        string? resolvedFor = null;
        ITenantSchedule schedule = Schedules.DailyAt(
            new TimeOnly(9, 0),
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

    [Fact]
    public async Task GetNextOccurrenceAsync_WhenLookupIsCanceled_ShouldObserveCancellation()
    {
        ITenantSchedule schedule = Schedules.DailyAt(
            new TimeOnly(9, 0),
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

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task GetNextOccurrenceAsync_WhenTenantIdIsBlank_ShouldThrow(string? tenantId)
    {
        ITenantSchedule schedule = Schedules.DailyAt(
            new TimeOnly(9, 0),
            (string tenant, CancellationToken cancellationToken) => ValueTask.FromResult<TimeZoneInfo?>(TimeZoneInfo.Utc));

        var act = async () =>
        {
            await schedule
                .GetNextOccurrenceAsync(tenantId!, new DateTimeOffset(2026, 1, 5, 8, 0, 0, TimeSpan.Zero), CancellationToken.None)
                .ConfigureAwait(false);
        };

        await act.Should().ThrowAsync<ArgumentException>().ConfigureAwait(false);
    }

    [Fact]
    public void DailyAt_WhenZoneOfIsNull_ShouldThrow()
    {
        var act = () => Schedules.DailyAt(
            new TimeOnly(9, 0),
            (Func<string, CancellationToken, ValueTask<TimeZoneInfo?>>) null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void WeeklyOn_WhenZoneOfIsNull_ShouldThrow()
    {
        var act = () => Schedules.WeeklyOn(
            DayOfWeek.Monday,
            new TimeOnly(9, 0),
            (Func<string, CancellationToken, ValueTask<TimeZoneInfo?>>) null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void WeeklyOn_WhenWeekdayIsUndefined_ShouldThrow()
    {
        var act = () => Schedules.WeeklyOn(
            (DayOfWeek) 42,
            new TimeOnly(9, 0),
            (string tenantId, CancellationToken cancellationToken) => ValueTask.FromResult<TimeZoneInfo?>(TimeZoneInfo.Utc));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static TimeZoneInfo CreateFixedZone(string id, TimeSpan offset)
    {
        return TimeZoneInfo.CreateCustomTimeZone(id, offset, id, id);
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
            "TenantSchedulesTests-Daylight",
            TimeSpan.FromHours(1),
            "Schedule test zone",
            "Test standard time",
            "Test daylight time",
            [rule]);
    }
}
