using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Commands.Recurring;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace LiteBus.Mediator.UnitTests.UseCases.Commands;

/// <summary>
///     Covers the recurring schedules: interval, daily, and weekly occurrences, daylight-saving resolution, and that a
///     scheduled loop waits for occurrences instead of the interval. Time never advances for real here.
/// </summary>
public sealed class RecurringScheduleTests
{
    [Fact]
    public void Every_returns_the_instant_one_interval_later()
    {
        var schedule = RecurringSchedules.Every(TimeSpan.FromMinutes(30));
        var after = new DateTimeOffset(2026, 1, 5, 8, 0, 0, TimeSpan.Zero);

        schedule.GetNextOccurrence(after).Should().Be(after.Add(TimeSpan.FromMinutes(30)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Every_rejects_a_non_positive_interval(int minutes)
    {
        var act = () => RecurringSchedules.Every(TimeSpan.FromMinutes(minutes));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void DailyAt_returns_today_when_the_time_is_ahead()
    {
        var schedule = RecurringSchedules.DailyAt(new TimeOnly(9, 0));
        var after = new DateTimeOffset(2026, 1, 5, 8, 0, 0, TimeSpan.Zero);

        schedule.GetNextOccurrence(after).Should().Be(new DateTimeOffset(2026, 1, 5, 9, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void DailyAt_returns_tomorrow_when_the_time_has_passed()
    {
        var schedule = RecurringSchedules.DailyAt(new TimeOnly(9, 0));
        var after = new DateTimeOffset(2026, 1, 5, 10, 0, 0, TimeSpan.Zero);

        schedule.GetNextOccurrence(after).Should().Be(new DateTimeOffset(2026, 1, 6, 9, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void DailyAt_returns_tomorrow_when_the_time_is_now()
    {
        var schedule = RecurringSchedules.DailyAt(new TimeOnly(9, 0));
        var after = new DateTimeOffset(2026, 1, 5, 9, 0, 0, TimeSpan.Zero);

        schedule.GetNextOccurrence(after).Should().Be(new DateTimeOffset(2026, 1, 6, 9, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void DailyAt_without_zone_reads_the_time_as_utc()
    {
        var schedule = RecurringSchedules.DailyAt(new TimeOnly(9, 0));

        // 10:00 at +02:00 is 08:00 UTC, so the 09:00 UTC occurrence is still ahead the same day.
        var after = new DateTimeOffset(2026, 1, 5, 10, 0, 0, TimeSpan.FromHours(2));

        schedule.GetNextOccurrence(after).Should().Be(new DateTimeOffset(2026, 1, 5, 9, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void WeeklyOn_returns_the_same_day_when_the_weekday_and_time_are_ahead()
    {
        var schedule = RecurringSchedules.WeeklyOn(DayOfWeek.Monday, new TimeOnly(9, 0));
        var after = new DateTimeOffset(2026, 1, 5, 8, 0, 0, TimeSpan.Zero);

        schedule.GetNextOccurrence(after).Should().Be(new DateTimeOffset(2026, 1, 5, 9, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void WeeklyOn_returns_next_week_when_the_time_has_passed()
    {
        var schedule = RecurringSchedules.WeeklyOn(DayOfWeek.Monday, new TimeOnly(9, 0));
        var after = new DateTimeOffset(2026, 1, 5, 10, 0, 0, TimeSpan.Zero);

        schedule.GetNextOccurrence(after).Should().Be(new DateTimeOffset(2026, 1, 12, 9, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void WeeklyOn_returns_the_coming_weekday()
    {
        var schedule = RecurringSchedules.WeeklyOn(DayOfWeek.Wednesday, new TimeOnly(9, 0));
        var after = new DateTimeOffset(2026, 1, 5, 8, 0, 0, TimeSpan.Zero);

        schedule.GetNextOccurrence(after).Should().Be(new DateTimeOffset(2026, 1, 7, 9, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void WeeklyOn_rejects_an_undefined_weekday()
    {
        var act = () => RecurringSchedules.WeeklyOn((DayOfWeek) 42, new TimeOnly(9, 0));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void DailyAt_resolves_a_daylight_saving_gap_to_the_next_valid_instant()
    {
        var schedule = RecurringSchedules.DailyAt(new TimeOnly(2, 30), CreateDaylightZone());

        // 2026-03-29 is the last Sunday of March 2026: clocks jump from 02:00 to 03:00.
        var after = new DateTimeOffset(2026, 3, 29, 0, 30, 0, TimeSpan.FromHours(1));

        schedule.GetNextOccurrence(after).Should().Be(new DateTimeOffset(2026, 3, 29, 3, 0, 0, TimeSpan.FromHours(2)));
    }

    [Fact]
    public void DailyAt_resolves_an_ambiguous_time_to_the_earlier_occurrence()
    {
        var schedule = RecurringSchedules.DailyAt(new TimeOnly(2, 30), CreateDaylightZone());

        // 2026-10-25 is the last Sunday of October 2026: clocks fall back from 03:00 to 02:00.
        var after = new DateTimeOffset(2026, 10, 25, 0, 30, 0, TimeSpan.FromHours(2));

        schedule.GetNextOccurrence(after).Should().Be(new DateTimeOffset(2026, 10, 25, 2, 30, 0, TimeSpan.FromHours(2)));
    }

    [Fact]
    public void WeeklyOn_resolves_a_daylight_saving_gap_to_the_next_valid_instant()
    {
        var schedule = RecurringSchedules.WeeklyOn(DayOfWeek.Sunday, new TimeOnly(2, 30), CreateDaylightZone());
        var after = new DateTimeOffset(2026, 3, 28, 12, 0, 0, TimeSpan.FromHours(1));

        schedule.GetNextOccurrence(after).Should().Be(new DateTimeOffset(2026, 3, 29, 3, 0, 0, TimeSpan.FromHours(2)));
    }

    [Fact]
    public async Task Scheduled_loop_waits_for_the_first_occurrence_then_sends()
    {
        var log = new ScheduleLog();
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 1, 5, 8, 0, 0, TimeSpan.Zero));
        await using var serviceProvider = BuildProvider(
            log,
            timeProvider,
            RecurringSchedules.DailyAt(new TimeOnly(9, 0)));

        var service = serviceProvider.GetRequiredService<RecurringCommandBackgroundService<ScheduleProbeCommand>>();
        using var cancellation = new CancellationTokenSource();

        var run = service.ExecuteAsync(cancellation.Token);

        // The occurrence is a fixed wall-clock instant, so advancing before the loop registers its wait still lands
        // short of it; the settle lets the loop register before the final advance moves past it.
        timeProvider.Advance(TimeSpan.FromMinutes(59));
        await Task.Delay(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);
        log.Count.Should().Be(0);

        timeProvider.Advance(TimeSpan.FromMinutes(2));
        await log.WaitForSendsAsync(1).ConfigureAwait(false);
        await cancellation.CancelAsync().ConfigureAwait(false);
        await run.ConfigureAwait(false);

        log.Count.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task Scheduled_loop_waits_for_the_next_occurrence_between_passes()
    {
        var log = new ScheduleLog();
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 1, 5, 8, 0, 0, TimeSpan.Zero));
        await using var serviceProvider = BuildProvider(
            log,
            timeProvider,
            RecurringSchedules.Every(TimeSpan.FromHours(1)));

        var service = serviceProvider.GetRequiredService<RecurringCommandBackgroundService<ScheduleProbeCommand>>();
        using var cancellation = new CancellationTokenSource();

        var run = service.ExecuteAsync(cancellation.Token);

        // The interval is five milliseconds, so reaching no send here proves the schedule paces the loop.
        await Task.Delay(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);
        log.Count.Should().Be(0);

        timeProvider.Advance(TimeSpan.FromHours(1));
        await log.WaitForSendsAsync(1).ConfigureAwait(false);

        await Task.Delay(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);
        timeProvider.Advance(TimeSpan.FromHours(1));
        await log.WaitForSendsAsync(2).ConfigureAwait(false);

        await cancellation.CancelAsync().ConfigureAwait(false);
        await run.ConfigureAwait(false);

        log.Count.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task Schedule_without_further_occurrence_ends_the_loop_without_sending()
    {
        var log = new ScheduleLog();
        await using var serviceProvider = BuildProvider(log, TimeProvider.System, new ExhaustedSchedule());

        var service = serviceProvider.GetRequiredService<RecurringCommandBackgroundService<ScheduleProbeCommand>>();

        await service.ExecuteAsync(CancellationToken.None).ConfigureAwait(false);

        log.Count.Should().Be(0);
    }

    private static ServiceProvider BuildProvider(ScheduleLog log, TimeProvider timeProvider, IRecurringSchedule schedule)
    {
        return new ServiceCollection().AddLiteBus(registry =>
        {
            registry.AddMessaging(messaging => messaging.UseTimeProvider(timeProvider));

            registry.AddCommands(commands => commands
                .Register<ScheduleProbeCommandHandler>()
                .AddRecurring(
                    () => new ScheduleProbeCommand(),
                    host =>
                    {
                        host.Interval = TimeSpan.FromMilliseconds(5);
                        host.Schedule = schedule;
                    }));
        }).AddSingleton(log).BuildServiceProvider();
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
            "RecurringScheduleTests-Daylight",
            TimeSpan.FromHours(1),
            "Recurring schedule test zone",
            "Test standard time",
            "Test daylight time",
            [rule]);
    }

    /// <summary>
    ///     Counts sends and lets a test wait for a number of them without racing a fixed delay.
    /// </summary>
    public sealed class ScheduleLog : IDisposable
    {
        /// <summary>
        ///     Signals each time a send completes so a waiter can re-check the count.
        /// </summary>
        private readonly SemaphoreSlim _signal = new(0);

        /// <summary>
        ///     The number of sends recorded so far.
        /// </summary>
        private int _count;

        /// <summary>
        ///     Gets the number of sends recorded so far.
        /// </summary>
        public int Count => Volatile.Read(ref _count);

        /// <summary>
        ///     Records one send.
        /// </summary>
        public void Record()
        {
            Interlocked.Increment(ref _count);
            _signal.Release();
        }

        /// <summary>
        ///     Waits until at least <paramref name="target" /> sends have been recorded.
        /// </summary>
        /// <param name="target">The number of sends to wait for.</param>
        /// <returns>A task that completes once the count is reached.</returns>
        public async Task WaitForSendsAsync(int target)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            while (Count < target)
            {
                await _signal.WaitAsync(timeout.Token).ConfigureAwait(false);
            }
        }

        /// <summary>
        ///     Releases the signal used to wait for sends.
        /// </summary>
        public void Dispose()
        {
            _signal.Dispose();
        }
    }

    /// <summary>
    ///     A command sent by the scheduled test loop.
    /// </summary>
    public sealed class ScheduleProbeCommand : ICommand;

    private sealed class ExhaustedSchedule : IRecurringSchedule
    {
        public DateTimeOffset? GetNextOccurrence(DateTimeOffset after)
        {
            return null;
        }
    }

    private sealed class ScheduleProbeCommandHandler : ICommandHandler<ScheduleProbeCommand>
    {
        private readonly ScheduleLog _log;

        public ScheduleProbeCommandHandler(ScheduleLog log)
        {
            _log = log;
        }

        public Task HandleAsync(ScheduleProbeCommand message, CancellationToken cancellationToken = default)
        {
            _log.Record();
            return Task.CompletedTask;
        }
    }
}
