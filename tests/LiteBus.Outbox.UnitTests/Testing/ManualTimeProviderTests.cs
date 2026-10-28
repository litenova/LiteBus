using LiteBus.Testing;

namespace LiteBus.Outbox.UnitTests.Testing;

/// <summary>
///     Verifies the controllable clock behavior of <see cref="ManualTimeProvider" />.
/// </summary>
[Collection("Sequential")]
public sealed class ManualTimeProviderTests : LiteBusTestBase
{
    /// <summary>
    ///     Verifies that advancing and replacing the clock are observed by readers.
    /// </summary>
    [Fact]
    public void GetUtcNow_should_reflect_advance_and_set_utc_now()
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var clock = new ManualTimeProvider(start);

        clock.Advance(TimeSpan.FromHours(1));
        clock.GetUtcNow().Should().Be(start.AddHours(1));

        clock.SetUtcNow(start.AddDays(1));
        clock.GetUtcNow().Should().Be(start.AddDays(1));
    }

    /// <summary>
    ///     Verifies that concurrent advances accumulate every tick without lost updates.
    /// </summary>
    [Fact]
    public async Task Advance_should_accumulate_every_tick_under_concurrent_use()
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var clock = new ManualTimeProvider(start);

        const int workerCount = 8;
        const int advancesPerWorker = 5000;

        var workers = new Task[workerCount];

        for (var worker = 0; worker < workerCount; worker++)
        {
            workers[worker] = Task.Run(() =>
            {
                for (var index = 0; index < advancesPerWorker; index++)
                {
                    clock.Advance(TimeSpan.FromTicks(1));
                }
            });
        }

        await Task.WhenAll(workers).ConfigureAwait(false);

        clock.GetUtcNow().Should().Be(start.AddTicks(workerCount * advancesPerWorker));
    }
}
