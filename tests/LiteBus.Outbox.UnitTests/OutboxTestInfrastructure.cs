using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox.Abstractions;

namespace LiteBus.Outbox.UnitTests;

internal static class OutboxTestInfrastructure
{
    internal sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;

        public ManualTimeProvider(DateTimeOffset initial)
        {
            _utcNow = initial;
        }

        public void Advance(TimeSpan amount)
        {
            _utcNow = _utcNow.Add(amount);
        }

        public void SetUtcNow(DateTimeOffset value)
        {
            _utcNow = value;
        }

        public override DateTimeOffset GetUtcNow()
        {
            return _utcNow;
        }
    }

    internal sealed class ThrowingOutboxLeaseStore : IOutboxLeaseStore
    {
        private readonly int _failuresBeforeSuccess;
        private int _attempts;
        private readonly OutboxTests.InMemoryOutboxStore _inner = new();

        public ThrowingOutboxLeaseStore(int failuresBeforeSuccess = int.MaxValue)
        {
            _failuresBeforeSuccess = failuresBeforeSuccess;
        }

        public OutboxTests.InMemoryOutboxStore Inner => _inner;

        public Task<IReadOnlyList<OutboxEnvelope>> LeasePendingAsync(
            OutboxLeaseRequest request,
            CancellationToken cancellationToken = default)
        {
            if (_attempts++ < _failuresBeforeSuccess)
            {
                throw new InvalidOperationException("Simulated lease store failure.");
            }

            return _inner.LeasePendingAsync(request, cancellationToken);
        }
    }

    /// <summary>
    ///     Test dispatcher that deserializes leased envelopes and records them for assertions.
    /// </summary>
    internal sealed class RecordingOutboxDispatcher : IOutboxDispatcher
    {
        /// <summary>
        ///     Gets the contract registry used to resolve persisted message types.
        /// </summary>
        private readonly IMessageContractRegistry _contractRegistry;

        /// <summary>
        ///     Gets the serializer used to hydrate stored payloads.
        /// </summary>
        private readonly IMessageSerializer _messageSerializer;

        /// <summary>
        ///     Gets the envelopes passed to <see cref="DispatchAsync" />.
        /// </summary>
        private readonly List<OutboxEnvelope> _dispatchedEnvelopes = [];

        /// <summary>
        ///     Gets the deserialized message instances produced during dispatch.
        /// </summary>
        private readonly List<object> _dispatchedMessages = [];

        /// <summary>
        ///     Initializes a new instance of the <see cref="RecordingOutboxDispatcher" /> class.
        /// </summary>
        /// <param name="contractRegistry">The contract registry used to resolve persisted message types.</param>
        /// <param name="messageSerializer">The serializer used to hydrate stored payloads.</param>
        public RecordingOutboxDispatcher(
            IMessageContractRegistry contractRegistry,
            IMessageSerializer messageSerializer)
        {
            _contractRegistry = contractRegistry ?? throw new ArgumentNullException(nameof(contractRegistry));
            _messageSerializer = messageSerializer ?? throw new ArgumentNullException(nameof(messageSerializer));
        }

        /// <summary>
        ///     Gets the envelopes passed to dispatch in invocation order.
        /// </summary>
        public IReadOnlyList<OutboxEnvelope> DispatchedEnvelopes => _dispatchedEnvelopes;

        /// <summary>
        ///     Gets the deserialized messages produced during dispatch in invocation order.
        /// </summary>
        public IReadOnlyList<object> DispatchedMessages => _dispatchedMessages;

        /// <inheritdoc />
        public async Task DispatchAsync(OutboxEnvelope message, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(message);

            _dispatchedEnvelopes.Add(message);

            var messageType = _contractRegistry.GetMessageType(message.ContractName, message.ContractVersion);
            var deserialized = await _messageSerializer.DeserializeAsync(messageType, message.Payload, cancellationToken)
                .ConfigureAwait(false);

            _dispatchedMessages.Add(deserialized);
        }
    }

    /// <summary>
    ///     Holds the recording dispatcher instance created during service provider construction.
    /// </summary>
    internal sealed class RecordingOutboxDispatcherHolder
    {
        /// <summary>
        ///     Gets or sets the recording dispatcher assigned when the service provider is built.
        /// </summary>
        public RecordingOutboxDispatcher? Instance { get; set; }
    }
}
