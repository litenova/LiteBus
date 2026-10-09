using LiteBus.Testing;
using LiteBus.Saga.Abstractions;

namespace LiteBus.Storage.UnitTests.Saga;

/// <summary>
///     Verifies saga purge bounds: an unbounded and unscoped purge is rejected, and a Take limit deletes the oldest
///     matches first.
/// </summary>
public sealed class SagaPurgeBoundTests
{
    /// <summary>
    ///     The base timestamp used for deterministic saga row timestamps.
    /// </summary>
    private static readonly DateTimeOffset BaseTime = new(2026, 7, 17, 8, 0, 0, TimeSpan.Zero);

    /// <summary>
    ///     Verifies a purge with no predicates and no Take limit is rejected.
    /// </summary>
    [Fact]
    public async Task PurgeAsync_WithEmptyFilter_ThrowsArgumentException()
    {
        var store = CreateStore();

        var action = () => store.PurgeAsync(new SagaPurgeFilter());

        await action.Should().ThrowAsync<ArgumentException>().ConfigureAwait(false);
    }

    /// <summary>
    ///     Verifies a non-positive Take limit is rejected.
    /// </summary>
    /// <param name="take">The invalid Take limit.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task PurgeAsync_WithNonPositiveTake_ThrowsArgumentOutOfRange(int take)
    {
        var store = CreateStore();

        var action = () => store.PurgeAsync(new SagaPurgeFilter { Take = take });

        await action.Should().ThrowAsync<ArgumentOutOfRangeException>().ConfigureAwait(false);
    }

    /// <summary>
    ///     Verifies a Take-only purge deletes at most Take rows, oldest first.
    /// </summary>
    [Fact]
    public async Task PurgeAsync_WithTake_DeletesOldestMatchesFirst()
    {
        var clock = new ManualTimeProvider(BaseTime);
        var store = CreateStore(clock);

        for (var index = 0; index < 15; index++)
        {
            await SaveNewAsync(store, $"order-{index:00}", index).ConfigureAwait(false);
            clock.Advance(TimeSpan.FromMinutes(1));
        }

        var removed = await store.PurgeAsync(new SagaPurgeFilter { Take = 10 }).ConfigureAwait(false);
        var remaining = await store.QueryAsync(new SagaQueryFilter { Take = 20 }).ConfigureAwait(false);

        removed.Should().Be(10);
        remaining.Select(summary => summary.Correlation.CorrelationId)
            .Should()
            .BeEquivalentTo("order-10", "order-11", "order-12", "order-13", "order-14");
    }

    /// <summary>
    ///     Verifies a CompletedBefore filter without Take deletes every match.
    /// </summary>
    [Fact]
    public async Task PurgeAsync_WithCompletedBeforeAndNoTake_DeletesEveryMatch()
    {
        var clock = new ManualTimeProvider(BaseTime);
        var store = CreateStore(clock);
        var firstOld = CreateCorrelation("old-1");
        var secondOld = CreateCorrelation("old-2");
        var thirdOld = CreateCorrelation("old-3");
        var recent = CreateCorrelation("recent");
        var active = CreateCorrelation("active");

        await SaveNewAsync(store, firstOld, 1).ConfigureAwait(false);
        await store.CompleteAsync(SagaCompleteItem.From(firstOld, 1)).ConfigureAwait(false);
        await SaveNewAsync(store, secondOld, 2).ConfigureAwait(false);
        await store.CompleteAsync(SagaCompleteItem.From(secondOld, 1)).ConfigureAwait(false);
        await SaveNewAsync(store, thirdOld, 3).ConfigureAwait(false);
        await store.CompleteAsync(SagaCompleteItem.From(thirdOld, 1)).ConfigureAwait(false);

        clock.Advance(TimeSpan.FromHours(2));
        var cutoff = clock.GetUtcNow();
        clock.Advance(TimeSpan.FromHours(1));

        await SaveNewAsync(store, recent, 4).ConfigureAwait(false);
        await store.CompleteAsync(SagaCompleteItem.From(recent, 1)).ConfigureAwait(false);
        await SaveNewAsync(store, active, 5).ConfigureAwait(false);

        var removed = await store.PurgeAsync(new SagaPurgeFilter
        {
            CompletedBefore = cutoff
        }).ConfigureAwait(false);
        var remaining = await store.QueryAsync(new SagaQueryFilter()).ConfigureAwait(false);

        removed.Should().Be(3);
        remaining.Select(summary => summary.Correlation).Should().BeEquivalentTo([recent, active]);
    }

    /// <summary>
    ///     Creates an in-memory saga store with an optional deterministic clock.
    /// </summary>
    /// <param name="clock">The optional clock used to stamp saga row timestamps.</param>
    /// <returns>The saga store.</returns>
    private static InMemorySagaStore CreateStore(TimeProvider? clock = null)
    {
        return new InMemorySagaStore(new SystemTextJsonMessageSerializer(), clock);
    }

    /// <summary>
    ///     Creates a saga correlation for one test row.
    /// </summary>
    /// <param name="correlationId">The correlation identifier.</param>
    /// <returns>The saga correlation.</returns>
    private static SagaCorrelation CreateCorrelation(string correlationId)
    {
        return new SagaCorrelation
        {
            CorrelationId = correlationId,
            SagaDefinitionId = "orders"
        };
    }

    /// <summary>
    ///     Saves a new saga row for one test correlation.
    /// </summary>
    /// <param name="store">The saga store.</param>
    /// <param name="correlation">The saga correlation.</param>
    /// <param name="step">The state step.</param>
    /// <returns>A task that completes when the save succeeds.</returns>
    private static Task SaveNewAsync(InMemorySagaStore store, SagaCorrelation correlation, int step)
    {
        return store.SaveAsync(SagaSaveItem<PurgeBoundSagaState>.From(
            correlation,
            new PurgeBoundSagaState { Step = step },
            0));
    }

    /// <summary>
    ///     Saves a new saga row for one test correlation identifier.
    /// </summary>
    /// <param name="store">The saga store.</param>
    /// <param name="correlationId">The correlation identifier.</param>
    /// <param name="step">The state step.</param>
    /// <returns>A task that completes when the save succeeds.</returns>
    private static Task SaveNewAsync(InMemorySagaStore store, string correlationId, int step)
    {
        return SaveNewAsync(store, CreateCorrelation(correlationId), step);
    }

    /// <summary>
    ///     Saga state tracked for purge bound tests.
    /// </summary>
    private sealed class PurgeBoundSagaState
    {
        /// <summary>
        ///     Gets or sets the current step.
        /// </summary>
        public int Step { get; set; }
    }
}
