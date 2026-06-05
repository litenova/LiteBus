using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging.Abstractions;
using LiteBus.Testing;

namespace LiteBus.Inbox.UnitTests;

/// <summary>
///     Verifies the inbox processor batches terminal state updates into single store calls.
/// </summary>
public sealed class InboxProcessorBulkTerminalStateTests
{
    /// <summary>
    ///     Confirms multiple dead-letter transitions are persisted through one bulk store call per pass.
    /// </summary>
    [Fact]
    public async Task ProcessPendingAsync_when_max_attempts_exceeded_should_call_bulk_move_to_dead_letter_once()
    {
        var inner = new InMemoryInboxStore();
        var terminal = new CountingInboxTerminalStateStore(inner);
        var clock = new InboxTestFixtures.FixedTimeProvider(new DateTimeOffset(2026, 6, 4, 12, 0, 0, TimeSpan.Zero));
        var dispatcher = new AlwaysFailingInboxDispatcher();

        var processor = new InboxProcessor(
            inner,
            terminal,
            dispatcher,
            new InboxProcessorOptions
            {
                BatchSize = 5,
                LeaseOwner = "bulk-test",
                LeaseDuration = TimeSpan.FromMinutes(1),
                Retry = new RetryOptions { MaxAttempts = 1, UseJitter = false }
            },
            clock);

        for (var index = 0; index < 3; index++)
        {
            await inner.EnqueueAsync(new InboxEnvelope
            {
                Id = Guid.NewGuid(),
                ContractName = "tests.commands.ship",
                ContractVersion = 1,
                Payload = "{}",
                CreatedAt = clock.GetUtcNow(),
                Status = InboxStatus.Pending,
                AttemptCount = 0
            });
        }

        var result = await processor.ProcessPendingAsync();

        result.DeadLetteredCount.Should().Be(3);
        terminal.BulkMoveToDeadLetterCallCount.Should().Be(1);
        terminal.SingleMoveToDeadLetterCallCount.Should().Be(0);
        terminal.LastBulkDeadLetterCount.Should().Be(3);
    }

    /// <summary>
    ///     Dispatcher that always fails so the processor records failures or dead letters.
    /// </summary>
    private sealed class AlwaysFailingInboxDispatcher : IInboxDispatcher
    {
        /// <inheritdoc />
        public Task DispatchAsync(InboxEnvelope envelope, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Dispatch failed.");
        }
    }

    /// <summary>
    ///     Terminal state store that counts bulk versus single dead-letter calls.
    /// </summary>
    private sealed class CountingInboxTerminalStateStore : IInboxTerminalStateStore
    {
        private readonly InMemoryInboxStore _inner;

        public CountingInboxTerminalStateStore(InMemoryInboxStore inner)
        {
            _inner = inner;
        }

        public int BulkMoveToDeadLetterCallCount { get; private set; }

        public int SingleMoveToDeadLetterCallCount { get; private set; }

        public int LastBulkDeadLetterCount { get; private set; }

        public Task MarkCompletedAsync(Guid messageId, CancellationToken cancellationToken = default)
        {
            return _inner.MarkCompletedAsync(messageId, cancellationToken);
        }

        public Task MarkFailedAsync(InboxEnvelopeFailure failure, CancellationToken cancellationToken = default)
        {
            return _inner.MarkFailedAsync(failure, cancellationToken);
        }

        public Task MoveToDeadLetterAsync(InboxEnvelopeDeadLetter deadLetter, CancellationToken cancellationToken = default)
        {
            SingleMoveToDeadLetterCallCount++;
            return _inner.MoveToDeadLetterAsync(deadLetter, cancellationToken);
        }

        public Task MarkCompletedAsync(IReadOnlyList<Guid> messageIds, CancellationToken cancellationToken = default)
        {
            return _inner.MarkCompletedAsync(messageIds, cancellationToken);
        }

        public Task MarkFailedAsync(IReadOnlyList<InboxEnvelopeFailure> failures, CancellationToken cancellationToken = default)
        {
            return _inner.MarkFailedAsync(failures, cancellationToken);
        }

        public Task MoveToDeadLetterAsync(IReadOnlyList<InboxEnvelopeDeadLetter> deadLetters, CancellationToken cancellationToken = default)
        {
            BulkMoveToDeadLetterCallCount++;
            LastBulkDeadLetterCount = deadLetters.Count;
            return _inner.MoveToDeadLetterAsync(deadLetters, cancellationToken);
        }

        public Task RequeueDeadLetterAsync(Guid messageId, CancellationToken cancellationToken = default)
        {
            return _inner.RequeueDeadLetterAsync(messageId, cancellationToken);
        }

        public Task RequeueDeadLetterAsync(IReadOnlyList<Guid> messageIds, CancellationToken cancellationToken = default)
        {
            return _inner.RequeueDeadLetterAsync(messageIds, cancellationToken);
        }
    }
}
