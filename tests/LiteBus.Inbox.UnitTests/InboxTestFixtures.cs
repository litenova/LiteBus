using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Inbox.UnitTests;

internal static class InboxTestFixtures
{
    internal sealed class StubInboxDispatcher : IInboxDispatcher
    {
        public Task DispatchAsync(InboxEnvelope envelope, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    internal sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedTimeProvider(DateTimeOffset now)
        {
            _now = now;
        }

        public override DateTimeOffset GetUtcNow()
        {
            return _now;
        }
    }

    internal sealed record ShipOrderCommand : ICommand
    {
        public Guid OrderId { get; init; }

        public required string IdempotencyKey { get; init; }
    }

    internal sealed class ShipOrderCommandHandler : ICommandHandler<ShipOrderCommand>
    {
        private readonly CommandRecorder _recorder;

        public ShipOrderCommandHandler(CommandRecorder recorder)
        {
            _recorder = recorder;
        }

        public Task HandleAsync(ShipOrderCommand message, CancellationToken cancellationToken = default)
        {
            _recorder.Record(message);
            return Task.CompletedTask;
        }
    }

    internal sealed class CommandRecorder
    {
        private readonly List<ShipOrderCommand> _commands = [];

        public IReadOnlyList<ShipOrderCommand> Commands => _commands;

        public void Record(ShipOrderCommand command)
        {
            _commands.Add(command);
        }
    }

    internal sealed record ArchiveCommand<T> : ICommand
    {
        public required T Value { get; init; }
    }

    internal sealed class ArchiveStringCommandHandler : ICommandHandler<ArchiveCommand<string>>
    {
        private readonly GenericCommandRecorder _recorder;

        public ArchiveStringCommandHandler(GenericCommandRecorder recorder)
        {
            _recorder = recorder;
        }

        public Task HandleAsync(ArchiveCommand<string> message, CancellationToken cancellationToken = default)
        {
            _recorder.Record(message.Value);
            return Task.CompletedTask;
        }
    }

    internal sealed class GenericCommandRecorder
    {
        private readonly List<string> _values = [];

        public IReadOnlyList<string> Values => _values;

        public void Record(string value)
        {
            _values.Add(value);
        }
    }

    internal sealed record GetOrderStatusCommand : ICommand<string>
    {
        public Guid OrderId { get; init; }
    }

    internal sealed record FaultyCommand : ICommand;

    internal sealed class FaultyCommandHandler : ICommandHandler<FaultyCommand>
    {
        public Task HandleAsync(FaultyCommand message, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Simulated handler failure.");
        }
    }

    internal sealed class IsInboxCapture
    {
        public bool IsInboxExecution { get; set; }
    }

    internal sealed class TraceMetadataCapture
    {
        public string? CorrelationId { get; set; }

        public string? CausationId { get; set; }

        public string? TenantId { get; set; }
    }

    internal sealed record InboxCheckCommand : ICommand;

    internal sealed class InboxCheckCommandHandler : ICommandHandler<InboxCheckCommand>
    {
        private readonly IsInboxCapture _capture;

        public InboxCheckCommandHandler(IsInboxCapture capture)
        {
            _capture = capture;
        }

        public Task HandleAsync(InboxCheckCommand message, CancellationToken cancellationToken = default)
        {
            _capture.IsInboxExecution =
                AmbientExecutionContext.Current.Items.TryGetValue(
                    InboxExecutionContextKeys.IsInboxExecution, out var value) &&
                value is true;

            return Task.CompletedTask;
        }
    }

    internal sealed class TraceMetadataCommandHandler : ICommandHandler<InboxCheckCommand>
    {
        private readonly TraceMetadataCapture _capture;

        public TraceMetadataCommandHandler(TraceMetadataCapture capture)
        {
            _capture = capture;
        }

        public Task HandleAsync(InboxCheckCommand message, CancellationToken cancellationToken = default)
        {
            var items = AmbientExecutionContext.Current.Items;
            _capture.CorrelationId = items.TryGetValue(MessageTraceContextKeys.CorrelationId, out var correlation)
                ? correlation as string
                : null;
            _capture.CausationId = items.TryGetValue(MessageTraceContextKeys.CausationId, out var causation)
                ? causation as string
                : null;
            _capture.TenantId = items.TryGetValue(MessageTraceContextKeys.TenantId, out var tenant)
                ? tenant as string
                : null;

            return Task.CompletedTask;
        }
    }

    internal sealed class FlakyInboxStateStore : IInboxStateStore
    {
        private readonly InMemoryInboxStore _inner;
        private readonly int _failCompletionsBeforeSuccess;
        private int _completionAttempts;

        public FlakyInboxStateStore(
            InMemoryInboxStore inner,
            int failCompletionsBeforeSuccess)
        {
            _inner = inner;
            _failCompletionsBeforeSuccess = failCompletionsBeforeSuccess;
        }

        public Task MarkCompletedAsync(Guid messageId, CancellationToken cancellationToken = default)
        {
            if (_completionAttempts++ < _failCompletionsBeforeSuccess)
            {
                throw new InvalidOperationException("Simulated completion failure.");
            }

            return _inner.MarkCompletedAsync(messageId, cancellationToken);
        }

        public Task MarkFailedAsync(InboxEnvelopeFailure failure, CancellationToken cancellationToken = default)
        {
            return _inner.MarkFailedAsync(failure, cancellationToken);
        }

        public Task MoveToDeadLetterAsync(InboxEnvelopeDeadLetter deadLetter, CancellationToken cancellationToken = default)
        {
            return _inner.MoveToDeadLetterAsync(deadLetter, cancellationToken);
        }
    }
}
