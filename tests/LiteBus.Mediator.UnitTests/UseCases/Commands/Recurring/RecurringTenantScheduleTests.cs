using System.Runtime.CompilerServices;
using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Commands.Recurring;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace LiteBus.Mediator.UnitTests.UseCases.Commands.Recurring;

/// <summary>
///     Covers per-tenant recurring schedules: per-zone occurrence computation, UTC fallback for unknown zones,
///     daylight-saving resolution, that a sweep sends only for due tenants and records each tenant occurrence, and
///     that a sweep prefers the tenant schedule over the shared schedule.
/// </summary>
public sealed class RecurringTenantScheduleTests
{
    [Fact]
    public async Task Tenant_daily_schedule_computes_each_tenants_occurrence_in_its_zone()
    {
        var betaZone = CreateFixedZone("TenantScheduleTests-PlusFive", TimeSpan.FromHours(5));
        ITenantRecurringSchedule schedule = RecurringSchedules
            .DailyAt(new TimeOnly(9, 0), ZoneOf(betaZone))
            .AsRecurringSchedule();
        var after = new DateTimeOffset(2026, 1, 5, 8, 0, 0, TimeSpan.Zero);

        var alpha = await schedule.GetNextOccurrenceAsync("alpha", after, CancellationToken.None).ConfigureAwait(false);
        var beta = await schedule.GetNextOccurrenceAsync("beta", after, CancellationToken.None).ConfigureAwait(false);

        alpha.Should().Be(new DateTimeOffset(2026, 1, 5, 9, 0, 0, TimeSpan.Zero));
        beta.Should().Be(new DateTimeOffset(2026, 1, 6, 4, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task Tenant_weekly_schedule_computes_each_tenants_occurrence_in_its_zone()
    {
        var betaZone = CreateFixedZone("TenantScheduleTests-PlusFive", TimeSpan.FromHours(5));
        ITenantRecurringSchedule schedule = RecurringSchedules
            .WeeklyOn(DayOfWeek.Monday, new TimeOnly(9, 0), ZoneOf(betaZone))
            .AsRecurringSchedule();

        // Monday 2026-01-05 08:00 UTC.
        var after = new DateTimeOffset(2026, 1, 5, 8, 0, 0, TimeSpan.Zero);

        var alpha = await schedule.GetNextOccurrenceAsync("alpha", after, CancellationToken.None).ConfigureAwait(false);
        var beta = await schedule.GetNextOccurrenceAsync("beta", after, CancellationToken.None).ConfigureAwait(false);

        alpha.Should().Be(new DateTimeOffset(2026, 1, 5, 9, 0, 0, TimeSpan.Zero));

        // 09:00 at +05:00 on Monday is 04:00 UTC Monday, already past, so the next one is a week later.
        beta.Should().Be(new DateTimeOffset(2026, 1, 12, 4, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task Tenant_schedule_with_unknown_zone_reads_the_time_as_utc()
    {
        ITenantRecurringSchedule schedule = RecurringSchedules
            .DailyAt(new TimeOnly(9, 0), (_, _) => new ValueTask<TimeZoneInfo?>(result: null))
            .AsRecurringSchedule();
        var after = new DateTimeOffset(2026, 1, 5, 8, 0, 0, TimeSpan.Zero);

        var occurrence = await schedule.GetNextOccurrenceAsync("alpha", after, CancellationToken.None).ConfigureAwait(false);

        occurrence.Should().Be(new DateTimeOffset(2026, 1, 5, 9, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task Tenant_daily_schedule_resolves_a_daylight_saving_gap_forward()
    {
        ITenantRecurringSchedule schedule = RecurringSchedules
            .DailyAt(new TimeOnly(2, 30), ZoneOf(CreateDaylightZone()))
            .AsRecurringSchedule();

        // 2026-03-29 is the last Sunday of March 2026: clocks jump from 02:00 to 03:00.
        var after = new DateTimeOffset(2026, 3, 29, 0, 30, 0, TimeSpan.FromHours(1));

        var occurrence = await schedule.GetNextOccurrenceAsync("beta", after, CancellationToken.None).ConfigureAwait(false);

        occurrence.Should().Be(new DateTimeOffset(2026, 3, 29, 3, 0, 0, TimeSpan.FromHours(2)));
    }

    [Fact]
    public async Task Sweep_sends_only_for_tenants_whose_occurrence_is_due()
    {
        var log = new TenantScheduleLog();
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 1, 5, 8, 0, 0, TimeSpan.Zero));
        var betaZone = CreateFixedZone("TenantScheduleTests-PlusFive", TimeSpan.FromHours(5));
        await using var serviceProvider = BuildProvider(
            log,
            timeProvider,
            ["alpha", "beta"],
            host =>
            {
                host.Interval = TimeSpan.FromMilliseconds(5);
                host.TenantSchedule = RecurringSchedules
                    .DailyAt(new TimeOnly(9, 0), ZoneOf(betaZone))
                    .AsRecurringSchedule();
            });

        var service = serviceProvider.GetRequiredService<RecurringTenantSweepBackgroundService<TenantScheduleCommand>>();
        using var cancellation = new CancellationTokenSource();

        var run = service.ExecuteAsync(cancellation.Token);

        // The settle lets the sweep compute its first wait from 08:59, so the final advance lands past the
        // 09:00 UTC occurrence but a day short of the tenant at +05:00.
        timeProvider.Advance(TimeSpan.FromMinutes(59));
        await Task.Delay(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);
        log.Attempts.Should().BeEmpty();

        timeProvider.Advance(TimeSpan.FromMinutes(2));
        await log.WaitForAttemptsAsync(1).ConfigureAwait(false);

        // The tenant at +05:00 is not due until the next day, so one pass must not send for it.
        await Task.Delay(TimeSpan.FromMilliseconds(200)).ConfigureAwait(false);
        log.Attempts.Select(attempt => attempt.Tenant).Should().Equal("alpha");

        var store = serviceProvider.GetRequiredService<IRecurringOccurrenceStore>();
        var loopName = typeof(TenantScheduleCommand).FullName!;
        var alphaLast = await store.GetLastAsync(loopName, "alpha", CancellationToken.None).ConfigureAwait(false);
        alphaLast.Should().NotBeNull();
        alphaLast!.ScheduledAt.Should().Be(new DateTimeOffset(2026, 1, 5, 9, 0, 0, TimeSpan.Zero));
        alphaLast.OccurrenceKey.Should().Be($"{loopName}/alpha/20260105T090000Z");

        var betaLast = await store.GetLastAsync(loopName, "beta", CancellationToken.None).ConfigureAwait(false);
        betaLast.Should().BeNull();

        timeProvider.Advance(TimeSpan.FromHours(19));
        await log.WaitForAttemptsAsync(2).ConfigureAwait(false);
        await cancellation.CancelAsync().ConfigureAwait(false);
        await run.ConfigureAwait(false);

        log.Attempts.Select(attempt => attempt.Tenant).Should().Equal("alpha", "beta");
    }

    [Fact]
    public async Task Sweep_prefers_the_tenant_schedule_over_the_shared_schedule()
    {
        var log = new TenantScheduleLog();
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 1, 5, 8, 0, 0, TimeSpan.Zero));
        await using var serviceProvider = BuildProvider(
            log,
            timeProvider,
            ["alpha"],
            host =>
            {
                host.Interval = TimeSpan.FromMilliseconds(5);

                // An exhausted shared schedule ends a loop that reads it, so a send proves the sweep read the
                // tenant schedule instead.
                host.Schedule = new ExhaustedSchedule();
                host.TenantSchedule = RecurringSchedules
                    .DailyAt(new TimeOnly(9, 0), ZoneOf(TimeZoneInfo.Utc))
                    .AsRecurringSchedule();
            });

        var service = serviceProvider.GetRequiredService<RecurringTenantSweepBackgroundService<TenantScheduleCommand>>();
        using var cancellation = new CancellationTokenSource();

        var run = service.ExecuteAsync(cancellation.Token);
        timeProvider.Advance(TimeSpan.FromMinutes(59));
        await Task.Delay(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);
        timeProvider.Advance(TimeSpan.FromMinutes(2));
        await log.WaitForAttemptsAsync(1).ConfigureAwait(false);
        await cancellation.CancelAsync().ConfigureAwait(false);
        await run.ConfigureAwait(false);

        log.Attempts.Select(attempt => attempt.Tenant).Should().Equal("alpha");
    }

    [Fact]
    public async Task Sweep_without_tenant_schedule_uses_the_shared_schedule()
    {
        var log = new TenantScheduleLog();
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 1, 5, 8, 0, 0, TimeSpan.Zero));
        await using var serviceProvider = BuildProvider(
            log,
            timeProvider,
            ["alpha", "beta"],
            host =>
            {
                host.Interval = TimeSpan.FromMilliseconds(5);
                host.Schedule = RecurringSchedules.DailyAt(new TimeOnly(9, 0));
            });

        var service = serviceProvider.GetRequiredService<RecurringTenantSweepBackgroundService<TenantScheduleCommand>>();
        using var cancellation = new CancellationTokenSource();

        var run = service.ExecuteAsync(cancellation.Token);

        // The interval is five milliseconds, so reaching the settle with no send proves the shared schedule
        // paces the sweep.
        timeProvider.Advance(TimeSpan.FromMinutes(59));
        await Task.Delay(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);
        log.Attempts.Should().BeEmpty();

        timeProvider.Advance(TimeSpan.FromMinutes(2));
        await log.WaitForAttemptsAsync(2).ConfigureAwait(false);
        await cancellation.CancelAsync().ConfigureAwait(false);
        await run.ConfigureAwait(false);

        log.Attempts.Select(attempt => attempt.Tenant).Should().Equal("alpha", "beta");
    }

    private static Func<string, CancellationToken, ValueTask<TimeZoneInfo?>> ZoneOf(TimeZoneInfo betaZone)
    {
        return (tenant, _) => new ValueTask<TimeZoneInfo?>(tenant == "beta" ? betaZone : TimeZoneInfo.Utc);
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
            "TenantScheduleTests-Daylight",
            TimeSpan.FromHours(1),
            "Tenant schedule test zone",
            "Test standard time",
            "Test daylight time",
            [rule]);
    }

    private static ServiceProvider BuildProvider(
        TenantScheduleLog log,
        TimeProvider timeProvider,
        string[] tenants,
        Action<RecurringCommandHostOptions> configure)
    {
        return new ServiceCollection().AddLiteBus(registry =>
        {
            registry.AddMessaging(messaging => messaging.UseTimeProvider(timeProvider));

            registry.AddCommands(commands => commands
                .Register<TenantScheduleCommandHandler>()
                .AddRecurringForEachTenant(
                    tenant => new TenantScheduleCommand { Tenant = tenant },
                    new ScriptedTenantSource(tenants),
                    configure));
        }).AddSingleton(log).BuildServiceProvider();
    }

    /// <summary>
    ///     Records the sweep attempts handlers observe and lets a test wait for a number of them.
    /// </summary>
    public sealed class TenantScheduleLog : IDisposable
    {
        /// <summary>
        ///     Signals each time an attempt completes so a waiter can re-check the count.
        /// </summary>
        private readonly SemaphoreSlim _signal = new(0);

        /// <summary>
        ///     The attempts observed so far, in the order they were recorded.
        /// </summary>
        private readonly List<TenantScheduleAttempt> _attempts = [];

        /// <summary>
        ///     Gets the attempts observed so far, in the order they were recorded.
        /// </summary>
        public IReadOnlyList<TenantScheduleAttempt> Attempts
        {
            get
            {
                lock (_attempts)
                {
                    return [.. _attempts];
                }
            }
        }

        /// <summary>
        ///     Records one attempt.
        /// </summary>
        /// <param name="attempt">The observed attempt.</param>
        public void Record(TenantScheduleAttempt attempt)
        {
            lock (_attempts)
            {
                _attempts.Add(attempt);
            }

            _signal.Release();
        }

        /// <summary>
        ///     Waits until at least <paramref name="target" /> attempts have been recorded.
        /// </summary>
        /// <param name="target">The number of attempts to wait for.</param>
        /// <returns>A task that completes once the count is reached.</returns>
        public async Task WaitForAttemptsAsync(int target)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            while (Attempts.Count < target)
            {
                await _signal.WaitAsync(timeout.Token).ConfigureAwait(false);
            }
        }

        /// <summary>
        ///     Releases the signal used to wait for attempts.
        /// </summary>
        public void Dispose()
        {
            _signal.Dispose();
        }
    }

    /// <summary>
    ///     One sweep attempt: the tenant the command was built for and the tenant the handler observed.
    /// </summary>
    public sealed record TenantScheduleAttempt(string Tenant, string? ObservedTenant);

    /// <summary>
    ///     A command built once per tenant by the sweep.
    /// </summary>
    public sealed class TenantScheduleCommand : ICommand
    {
        public string Tenant { get; init; } = "";
    }

    private sealed class TenantScheduleCommandHandler : ICommandHandler<TenantScheduleCommand>
    {
        private readonly TenantScheduleLog _log;

        public TenantScheduleCommandHandler(TenantScheduleLog log)
        {
            _log = log;
        }

        public Task HandleAsync(TenantScheduleCommand message, CancellationToken cancellationToken = default)
        {
            _log.Record(new TenantScheduleAttempt(message.Tenant, AmbientExecutionContext.Current.Identity.TenantId));
            return Task.CompletedTask;
        }
    }

    private sealed class ExhaustedSchedule : IRecurringSchedule
    {
        public DateTimeOffset? GetNextOccurrence(DateTimeOffset after)
        {
            return null;
        }
    }

    private sealed class ScriptedTenantSource : IRecurringTenantSource
    {
        private readonly string[] _tenants;

        public ScriptedTenantSource(string[] tenants)
        {
            _tenants = tenants;
        }

        public async IAsyncEnumerable<string> GetTenantsAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask.ConfigureAwait(false);

            foreach (var tenant in _tenants)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return tenant;
            }
        }
    }
}
