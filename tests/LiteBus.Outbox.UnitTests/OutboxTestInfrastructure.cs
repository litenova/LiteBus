using System;
using System.Linq;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.InMemory;
using LiteBus.Runtime.Abstractions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LiteBus.Outbox.UnitTests;

internal static class OutboxTestInfrastructure
{
    /// <summary>
    ///     Resolves the generic-host adapter for <see cref="OutboxProcessorBackgroundService" />.
    /// </summary>
    /// <param name="provider">The service provider built with <c>AddLiteBus</c> and an enabled outbox processor.</param>
    /// <returns>The <see cref="IHostedService" /> that runs the outbox processor loop.</returns>
    internal static IHostedService GetOutboxProcessorHostedService(IServiceProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        var manifest = provider.GetRequiredService<LiteBusHostManifest>();
        var processorIndex = manifest.BackgroundServices.ToList().IndexOf(typeof(OutboxProcessorBackgroundService));
        if (processorIndex < 0)
        {
            throw new InvalidOperationException(
                "Outbox processor background service is not registered in the LiteBus host manifest.");
        }

        var hostedServices = provider.GetServices<IHostedService>().ToList();
        var backgroundServiceOffset = manifest.StartupTasks.Count > 0 ? 1 : 0;

        return hostedServices[backgroundServiceOffset + processorIndex];
    }

    /// <summary>
    ///     Starts every LiteBus <see cref="IHostedService" /> so startup tasks unblock background loops.
    /// </summary>
    /// <param name="provider">The service provider built with <c>AddLiteBus</c>.</param>
    /// <param name="cancellationToken">A token that cancels host startup.</param>
    /// <returns>A task that completes after each hosted service has started.</returns>
    internal static async Task StartLiteBusHostedServicesAsync(
        IServiceProvider provider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provider);

        foreach (var hostedService in provider.GetServices<IHostedService>())
        {
            await hostedService.StartAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Stops every LiteBus <see cref="IHostedService" /> in reverse registration order.
    /// </summary>
    /// <param name="provider">The service provider built with <c>AddLiteBus</c>.</param>
    /// <param name="cancellationToken">A token that cancels host shutdown.</param>
    /// <returns>A task that completes after each hosted service has stopped.</returns>
    internal static async Task StopLiteBusHostedServicesAsync(
        IServiceProvider provider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provider);

        var hostedServices = provider.GetServices<IHostedService>().ToList();
        for (var index = hostedServices.Count - 1; index >= 0; index--)
        {
            await hostedServices[index].StopAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    internal sealed class ThrowingOutboxLeaseStore : IOutboxLeaseStore
    {
        private readonly int _failuresBeforeSuccess;
        private int _attempts;
        private readonly InMemoryOutboxStore _inner = new();

        public ThrowingOutboxLeaseStore(int failuresBeforeSuccess = int.MaxValue)
        {
            _failuresBeforeSuccess = failuresBeforeSuccess;
        }

        public InMemoryOutboxStore Inner => _inner;

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

        public Task<bool> RenewLeaseAsync(
            Guid messageId,
            string leaseOwner,
            DateTimeOffset expiresAt,
            CancellationToken cancellationToken = default) =>
            _inner.RenewLeaseAsync(messageId, leaseOwner, expiresAt, cancellationToken);
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
