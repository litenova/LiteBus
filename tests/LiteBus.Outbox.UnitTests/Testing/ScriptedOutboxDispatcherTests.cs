using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox.Abstractions;
using LiteBus.Testing;
using OrderPlacedEvent = LiteBus.Outbox.UnitTests.Testing.OutboxTestingHarness.OrderPlacedEvent;
using PaymentTakenEvent = LiteBus.Outbox.UnitTests.Testing.OutboxTestingHarness.PaymentTakenEvent;

namespace LiteBus.Outbox.UnitTests.Testing;

/// <summary>
///     Verifies per-contract dispatch scripts, attempt counting, and delegation in <see cref="ScriptedOutboxDispatcher" />.
/// </summary>
[Collection("Sequential")]
public sealed class ScriptedOutboxDispatcherTests : LiteBusTestBase
{
    /// <summary>
    ///     Verifies that a failing contract retries and then publishes while the untouched contract publishes at once.
    /// </summary>
    [Fact]
    public async Task FailNext_should_retry_then_publish_scripted_contract()
    {
        var harness = new OutboxTestingHarness(new RetryOptions
        {
            InitialDelay = TimeSpan.Zero,
            UseJitter = false
        });
        harness.Dispatcher.FailNext("orders.placed", new InvalidOperationException("dispatch failed"), times: 2);

        var orderId = Guid.NewGuid();
        var orderReceipt = await harness.Writer.EnqueueAsync(
            OutboxEnqueueItem<OrderPlacedEvent>.From(new OrderPlacedEvent { OrderId = orderId })).ConfigureAwait(false);
        var paymentReceipt = await harness.Writer.EnqueueAsync(
            OutboxEnqueueItem<PaymentTakenEvent>.From(new PaymentTakenEvent { OrderId = orderId })).ConfigureAwait(false);

        var drain = await ProcessorPassHelpers.DrainOutboxAsync(harness.BuildProvider()).ConfigureAwait(false);

        drain.Passes.Should().Be(4);
        drain.LeasedCount.Should().Be(4);
        drain.SucceededCount.Should().Be(2);
        drain.FailedCount.Should().Be(2);
        harness.Store.Get(orderReceipt.Id).Status.Should().Be(OutboxStatus.Published);
        harness.Store.Get(orderReceipt.Id).AttemptCount.Should().Be(3);
        harness.Store.Get(paymentReceipt.Id).Status.Should().Be(OutboxStatus.Published);
        harness.Store.Get(paymentReceipt.Id).AttemptCount.Should().Be(1);
        harness.Dispatcher.GetAttemptCount("orders.placed").Should().Be(3);
        harness.Dispatcher.GetAttemptCount("payments.taken").Should().Be(1);
        harness.Dispatcher.AttemptedEnvelopes.Should().HaveCount(4);
        harness.Dispatcher.DispatchedEnvelopes.Should().HaveCount(2);
    }

    /// <summary>
    ///     Verifies that an always-failing contract dead-letters after exhausting attempts while the other contract publishes.
    /// </summary>
    [Fact]
    public async Task FailAlways_should_dead_letter_scripted_contract_while_other_contract_publishes()
    {
        var harness = new OutboxTestingHarness(new RetryOptions
        {
            MaxAttempts = 3,
            InitialDelay = TimeSpan.Zero,
            UseJitter = false
        });
        harness.Dispatcher.FailAlways("orders.placed", new InvalidOperationException("dispatch failed"));

        var orderId = Guid.NewGuid();
        var orderReceipt = await harness.Writer.EnqueueAsync(
            OutboxEnqueueItem<OrderPlacedEvent>.From(new OrderPlacedEvent { OrderId = orderId })).ConfigureAwait(false);
        var paymentReceipt = await harness.Writer.EnqueueAsync(
            OutboxEnqueueItem<PaymentTakenEvent>.From(new PaymentTakenEvent { OrderId = orderId })).ConfigureAwait(false);

        var drain = await ProcessorPassHelpers.DrainOutboxAsync(harness.BuildProvider()).ConfigureAwait(false);

        drain.SucceededCount.Should().Be(1);
        drain.DeadLetteredCount.Should().Be(1);
        var failed = harness.Store.Get(orderReceipt.Id);
        failed.Status.Should().Be(OutboxStatus.DeadLettered);
        failed.AttemptCount.Should().Be(3);
        failed.LastError.Should().Contain("dispatch failed");
        harness.Store.Get(paymentReceipt.Id).Status.Should().Be(OutboxStatus.Published);
        harness.Dispatcher.GetAttemptCount("orders.placed").Should().Be(3);
        harness.Dispatcher.GetAttemptCount("payments.taken").Should().Be(1);
    }

    /// <summary>
    ///     Verifies that an unscripted dispatch reaches the inner dispatcher and is recorded as dispatched.
    /// </summary>
    [Fact]
    public async Task DispatchAsync_without_script_should_delegate_to_inner()
    {
        var inner = new RecordingDispatcher();
        var dispatcher = new ScriptedOutboxDispatcher(inner);
        var envelope = PendingEnvelope("orders.placed");

        await dispatcher.DispatchAsync(envelope).ConfigureAwait(false);

        inner.Dispatched.Should().ContainSingle().Which.Should().Be(envelope);
        dispatcher.DispatchedEnvelopes.Should().ContainSingle().Which.Should().Be(envelope);
        dispatcher.GetAttemptCount("orders.placed").Should().Be(1);
    }

    /// <summary>
    ///     Verifies that clearing removes scripts, recordings, and attempt counts.
    /// </summary>
    [Fact]
    public async Task Clear_should_reset_scripts_counts_and_recordings()
    {
        var dispatcher = new ScriptedOutboxDispatcher();
        dispatcher.FailAlways("orders.placed", new InvalidOperationException("dispatch failed"));
        var envelope = PendingEnvelope("orders.placed");

        Func<Task> failing = () => dispatcher.DispatchAsync(envelope);
        await failing.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);
        dispatcher.GetAttemptCount("orders.placed").Should().Be(1);

        dispatcher.Clear();

        dispatcher.GetAttemptCount("orders.placed").Should().Be(0);
        dispatcher.AttemptedEnvelopes.Should().BeEmpty();

        await dispatcher.DispatchAsync(envelope).ConfigureAwait(false);

        dispatcher.DispatchedEnvelopes.Should().ContainSingle();
    }

    /// <summary>
    ///     Verifies that a canceled dispatch throws cancellation without recording an attempt or calling inner.
    /// </summary>
    [Fact]
    public async Task DispatchAsync_when_cancelled_should_throw_without_recording_or_delegating()
    {
        var inner = new RecordingDispatcher();
        var dispatcher = new ScriptedOutboxDispatcher(inner);
        dispatcher.FailAlways("orders.placed", new InvalidOperationException("dispatch failed"));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Func<Task> act = () => dispatcher.DispatchAsync(PendingEnvelope("orders.placed"), cancelled.Token);
        await act.Should().ThrowAsync<OperationCanceledException>().ConfigureAwait(false);

        inner.Dispatched.Should().BeEmpty();
        dispatcher.AttemptedEnvelopes.Should().BeEmpty();
        dispatcher.GetAttemptCount("orders.placed").Should().Be(0);
    }

    /// <summary>
    ///     Builds a pending envelope for direct dispatcher calls.
    /// </summary>
    /// <param name="contractName">The envelope contract name.</param>
    /// <returns>The pending envelope.</returns>
    private static OutboxEnvelope PendingEnvelope(string contractName)
    {
        return new OutboxEnvelope
        {
            Id = Guid.NewGuid(),
            ContractName = contractName,
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = new DateTimeOffset(2026, 6, 6, 8, 0, 0, TimeSpan.Zero),
            Status = OutboxStatus.Pending,
            AttemptCount = 0
        };
    }

    /// <summary>
    ///     Records envelopes received by the inner dispatcher.
    /// </summary>
    private sealed class RecordingDispatcher : IOutboxDispatcher
    {
        /// <summary>
        ///     Gets the envelopes received in call order.
        /// </summary>
        public List<OutboxEnvelope> Dispatched { get; } = [];

        /// <inheritdoc />
        public Task DispatchAsync(OutboxEnvelope message, CancellationToken cancellationToken = default)
        {
            Dispatched.Add(message);
            return Task.CompletedTask;
        }
    }
}
