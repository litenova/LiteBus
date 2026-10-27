using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox.Abstractions;
using LiteBus.Testing;
using OrderPlacedEvent = LiteBus.Outbox.UnitTests.Testing.OutboxTestingHarness.OrderPlacedEvent;
using PaymentTakenEvent = LiteBus.Outbox.UnitTests.Testing.OutboxTestingHarness.PaymentTakenEvent;

namespace LiteBus.Outbox.UnitTests.Testing;

/// <summary>
///     Verifies immediate processor drains through <see cref="ProcessorPassHelpers.DrainOutboxAsync(IServiceProvider, int, CancellationToken)" />.
/// </summary>
[Collection("Sequential")]
public sealed class OutboxDrainTests : LiteBusTestBase
{
    /// <summary>
    ///     Verifies that one drain call publishes every pending message across multiple batches.
    /// </summary>
    [Fact]
    public async Task DrainOutboxAsync_should_publish_everything_pending_in_one_call()
    {
        var harness = new OutboxTestingHarness(new RetryOptions { UseJitter = false }, batchSize: 2);

        for (var index = 0; index < 5; index++)
        {
            await harness.Writer.EnqueueAsync(
                OutboxEnqueueItem<PaymentTakenEvent>.From(new PaymentTakenEvent { OrderId = Guid.NewGuid() })).ConfigureAwait(false);
        }

        var drain = await ProcessorPassHelpers.DrainOutboxAsync(harness.BuildProvider()).ConfigureAwait(false);

        drain.Passes.Should().Be(4);
        drain.LeasedCount.Should().Be(5);
        drain.SucceededCount.Should().Be(5);
        drain.FailedCount.Should().Be(0);
        drain.DeadLetteredCount.Should().Be(0);
        harness.Store.GetAll(OutboxStatus.Published).Should().HaveCount(5);
        harness.Dispatcher.DispatchedEnvelopes.Should().HaveCount(5);
    }

    /// <summary>
    ///     Verifies that draining an idle queue returns one empty pass.
    /// </summary>
    [Fact]
    public async Task DrainOutboxAsync_when_idle_should_return_single_empty_pass()
    {
        var harness = new OutboxTestingHarness(new RetryOptions { UseJitter = false });

        var drain = await ProcessorPassHelpers.DrainOutboxAsync(harness.BuildProvider()).ConfigureAwait(false);

        drain.Passes.Should().Be(1);
        drain.LeasedCount.Should().Be(0);
        drain.SucceededCount.Should().Be(0);
        drain.FailedCount.Should().Be(0);
        drain.DeadLetteredCount.Should().Be(0);
    }

    /// <summary>
    ///     Verifies that a drain fails when passes keep leasing messages past the pass budget.
    /// </summary>
    [Fact]
    public async Task DrainOutboxAsync_when_work_remains_after_max_passes_should_throw()
    {
        var harness = new OutboxTestingHarness(new RetryOptions
        {
            MaxAttempts = 5,
            InitialDelay = TimeSpan.Zero,
            UseJitter = false
        });
        harness.Dispatcher.FailAlways("orders.placed", new InvalidOperationException("dispatch failed"));

        await harness.Writer.EnqueueAsync(
            OutboxEnqueueItem<OrderPlacedEvent>.From(new OrderPlacedEvent { OrderId = Guid.NewGuid() })).ConfigureAwait(false);

        Func<Task> act = () => ProcessorPassHelpers.DrainOutboxAsync(harness.BuildProvider(), maxPasses: 2);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*still leased*").ConfigureAwait(false);
    }

    /// <summary>
    ///     Verifies that a backoff-delayed failure is picked up by a later drain after the clock advances.
    /// </summary>
    [Fact]
    public async Task DrainOutboxAsync_with_retry_backoff_should_resume_after_clock_advance()
    {
        var harness = new OutboxTestingHarness(new RetryOptions
        {
            MaxAttempts = 3,
            InitialDelay = TimeSpan.FromSeconds(60),
            UseJitter = false
        });
        harness.Dispatcher.FailNext("orders.placed", new InvalidOperationException("dispatch failed"));

        var orderId = Guid.NewGuid();
        var orderReceipt = await harness.Writer.EnqueueAsync(
            OutboxEnqueueItem<OrderPlacedEvent>.From(new OrderPlacedEvent { OrderId = orderId })).ConfigureAwait(false);
        await harness.Writer.EnqueueAsync(
            OutboxEnqueueItem<PaymentTakenEvent>.From(new PaymentTakenEvent { OrderId = orderId })).ConfigureAwait(false);

        var provider = harness.BuildProvider();
        var first = await ProcessorPassHelpers.DrainOutboxAsync(provider).ConfigureAwait(false);

        first.LeasedCount.Should().Be(2);
        first.SucceededCount.Should().Be(1);
        first.FailedCount.Should().Be(1);
        harness.Store.Get(orderReceipt.Id).Status.Should().Be(OutboxStatus.Failed);

        harness.Clock.Advance(TimeSpan.FromSeconds(61));
        var second = await ProcessorPassHelpers.DrainOutboxAsync(provider).ConfigureAwait(false);

        second.SucceededCount.Should().Be(1);
        var retried = harness.Store.Get(orderReceipt.Id);
        retried.Status.Should().Be(OutboxStatus.Published);
        retried.AttemptCount.Should().Be(2);
    }
}
