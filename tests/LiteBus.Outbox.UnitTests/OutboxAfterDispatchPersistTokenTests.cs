using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Processing;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.InMemory;

namespace LiteBus.Outbox.UnitTests;

/// <summary>
///     Verifies the AfterDispatch hook runs on the persist token instead of the shutdown token.
/// </summary>
public sealed class OutboxAfterDispatchPersistTokenTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 6, 6, 8, 0, 0, TimeSpan.Zero);

    /// <summary>
    ///     Verifies a hook that throws when the pass token fires still ends with the row in its terminal status.
    /// </summary>
    [Fact]
    public async Task ProcessPendingAsync_when_pass_token_fires_during_hook_should_still_persist_terminal_outcome()
    {
        var store = new InMemoryOutboxStore();
        using var cancellation = new CancellationTokenSource();
        var hook = new PassTokenThrowingAfterDispatchHook();

        var processor = new PipelinedOutboxProcessor(
            store,
            store,
            new CancellingPassDispatcher(cancellation),
            new OutboxProcessorOptions
            {
                BatchSize = 1,
                LeaseOwner = "hook-token-worker",
                LeaseDuration = TimeSpan.FromMinutes(1),
                DispatcherConcurrency = 1,
                Retry = new RetryOptions { UseJitter = false }
            },
            TimeProvider.System,
            [hook]);

        var messageId = Guid.NewGuid();

        await store.AddAsync(new OutboxEnvelope
        {
            Id = messageId,
            ContractName = "orders.events.submitted",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = BaseTime,
            AttemptCount = 0,
            Status = OutboxStatus.Pending
        }).ConfigureAwait(false);

        var act = () => processor.ProcessPendingAsync(cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>().ConfigureAwait(false);

        store.Get(messageId).Status.Should().Be(OutboxStatus.Published);
    }

    /// <summary>
    ///     Cancels the pass during dispatch and then completes dispatch normally.
    /// </summary>
    private sealed class CancellingPassDispatcher : IOutboxDispatcher
    {
        private readonly CancellationTokenSource _cancellation;

        /// <summary>
        ///     Initializes a new instance of the <see cref="CancellingPassDispatcher" /> class.
        /// </summary>
        /// <param name="cancellation">The pass token source cancelled on dispatch.</param>
        public CancellingPassDispatcher(CancellationTokenSource cancellation)
        {
            ArgumentNullException.ThrowIfNull(cancellation);
            _cancellation = cancellation;
        }

        /// <inheritdoc />
        public Task DispatchAsync(OutboxEnvelope envelope, CancellationToken cancellationToken = default)
        {
            _cancellation.Cancel();
            return Task.CompletedTask;
        }
    }

    /// <summary>
    ///     Throws only when the token handed to the hook is already cancelled.
    /// </summary>
    private sealed class PassTokenThrowingAfterDispatchHook : IProcessorEnvelopeHook
    {
        /// <inheritdoc />
        public Task BeforeDispatchAsync(IProcessorEnvelope envelope, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task AfterDispatchAsync(IProcessorEnvelope envelope, CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            return Task.CompletedTask;
        }
    }
}
