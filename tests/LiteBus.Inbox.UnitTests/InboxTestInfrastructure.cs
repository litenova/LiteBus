using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging.Abstractions;
using LiteBus.Runtime.Abstractions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LiteBus.Inbox.UnitTests;

internal static class InboxTestInfrastructure
{
    /// <summary>
    ///     Registers a test <see cref="IInboxDispatcher" /> that deserializes envelopes and executes them through
    ///     <see cref="ICommandMediator" />.
    /// </summary>
    /// <param name="services">The service collection under test.</param>
    /// <returns>The same service collection for chaining.</returns>
    internal static IServiceCollection AddCommandMediatorInboxDispatcher(this IServiceCollection services)
    {
        return services.AddSingleton<IInboxDispatcher, CommandMediatorInboxDispatcher>();
    }

    /// <summary>
    ///     Creates a processing store from separate lease and state writer roles.
    /// </summary>
    /// <param name="leaseStore">The store role used to lease due envelopes.</param>
    /// <param name="stateWriter">The store role used to persist post-transition envelopes.</param>
    /// <returns>The composite processing store passed to inbox processors.</returns>
    internal static IInboxProcessingStore CreateProcessingStore(
        IInboxLeaseStore leaseStore,
        IInboxStateWriter stateWriter) =>
        new SplitInboxProcessingStore(leaseStore, stateWriter);

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

    /// <summary>
    ///     Resolves the generic-host adapter for <see cref="InboxProcessorBackgroundService" />.
    /// </summary>
    /// <param name="provider">The service provider built with <c>AddLiteBus</c> and an enabled inbox processor.</param>
    /// <returns>The <see cref="IHostedService" /> that runs the inbox processor loop.</returns>
    internal static IHostedService GetInboxProcessorHostedService(IServiceProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        var manifest = provider.GetRequiredService<LiteBusHostManifest>();
        var processorIndex = -1;
        for (var index = 0; index < manifest.BackgroundServices.Count; index++)
        {
            if (manifest.BackgroundServices[index] == typeof(InboxProcessorBackgroundService))
            {
                processorIndex = index;
                break;
            }
        }

        if (processorIndex < 0)
        {
            throw new InvalidOperationException(
                "Inbox processor background service is not registered in the LiteBus host manifest.");
        }

        var hostedServices = provider.GetServices<IHostedService>().ToList();
        var backgroundServiceOffset = manifest.StartupTasks.Count > 0 ? 1 : 0;

        return hostedServices[backgroundServiceOffset + processorIndex];
    }

    internal sealed class ThrowingInboxLeaseStore : IInboxLeaseStore
    {
        private readonly int _failuresBeforeSuccess;
        private int _attempts;
        private readonly InMemoryInboxStore _inner = new();

        public ThrowingInboxLeaseStore(int failuresBeforeSuccess = int.MaxValue)
        {
            _failuresBeforeSuccess = failuresBeforeSuccess;
        }

        public InMemoryInboxStore Inner => _inner;

        public Task<IReadOnlyList<InboxEnvelope>> LeasePendingAsync(
            InboxLeaseRequest request,
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

    internal sealed class DelegatingInboxLeaseStore : IInboxLeaseStore
    {
        private readonly InMemoryInboxStore _inner;
        private readonly Func<InboxLeaseRequest, IReadOnlyList<InboxEnvelope>>? _onLease;

        public DelegatingInboxLeaseStore(
            InMemoryInboxStore inner,
            Func<InboxLeaseRequest, IReadOnlyList<InboxEnvelope>>? onLease = null)
        {
            _inner = inner;
            _onLease = onLease;
        }

        public async Task<IReadOnlyList<InboxEnvelope>> LeasePendingAsync(
            InboxLeaseRequest request,
            CancellationToken cancellationToken = default)
        {
            var leased = await _inner.LeasePendingAsync(request, cancellationToken).ConfigureAwait(false);
            return _onLease?.Invoke(request) ?? leased;
        }

        public Task<bool> RenewLeaseAsync(
            Guid messageId,
            string leaseOwner,
            DateTimeOffset expiresAt,
            CancellationToken cancellationToken = default) =>
            _inner.RenewLeaseAsync(messageId, leaseOwner, expiresAt, cancellationToken);
    }

    /// <summary>
    ///     Test dispatcher that routes deserialized inbox envelopes through the command mediator.
    /// </summary>
    internal sealed class CommandMediatorInboxDispatcher : IInboxDispatcher
    {
        /// <summary>
        ///     Gets the command mediator used to execute deserialized commands.
        /// </summary>
        private readonly ICommandMediator _commandMediator;

        /// <summary>
        ///     Gets the registry used to resolve persisted contracts back to CLR types.
        /// </summary>
        private readonly IMessageContractRegistry _contractRegistry;

        /// <summary>
        ///     Gets the serializer used to hydrate envelope payloads.
        /// </summary>
        private readonly IMessageSerializer _messageSerializer;

        /// <summary>
        ///     Initializes a new instance of the <see cref="CommandMediatorInboxDispatcher" /> class.
        /// </summary>
        /// <param name="commandMediator">The command mediator used to execute deserialized commands.</param>
        /// <param name="contractRegistry">The registry used to resolve persisted contracts back to CLR types.</param>
        /// <param name="messageSerializer">The serializer used to hydrate envelope payloads.</param>
        public CommandMediatorInboxDispatcher(
            ICommandMediator commandMediator,
            IMessageContractRegistry contractRegistry,
            IMessageSerializer messageSerializer)
        {
            _commandMediator = commandMediator ?? throw new ArgumentNullException(nameof(commandMediator));
            _contractRegistry = contractRegistry ?? throw new ArgumentNullException(nameof(contractRegistry));
            _messageSerializer = messageSerializer ?? throw new ArgumentNullException(nameof(messageSerializer));
        }

        /// <inheritdoc />
        public async Task DispatchAsync(InboxEnvelope envelope, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(envelope);

            var messageType = _contractRegistry.GetMessageType(envelope.ContractName, envelope.ContractVersion);
            var message = await _messageSerializer.DeserializeAsync(messageType, envelope.Payload, cancellationToken).ConfigureAwait(false);

            if (message is not ICommand command)
            {
                throw new InvalidOperationException(
                    $"Contract '{envelope.ContractName}' version {envelope.ContractVersion} resolved to a type that does not implement ICommand.");
            }

            var mediationSettings = new CommandMediationSettings();
            mediationSettings.Items[InboxExecutionContextKeys.IsInboxExecution] = true;
            MessageProcessorDiagnostics.ApplyTraceMetadata(
                mediationSettings.Items,
                envelope.CorrelationId,
                envelope.CausationId,
                envelope.TenantId);

            await _commandMediator.SendAsync(command, mediationSettings, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Adapts separate lease and state writer roles to <see cref="IInboxProcessingStore" />.
    /// </summary>
    private sealed class SplitInboxProcessingStore : IInboxProcessingStore
    {
        /// <summary>
        ///     The store role used to accept new envelopes.
        /// </summary>
        private readonly IInboxStore _store;

        /// <summary>
        ///     The store role used to lease due envelopes.
        /// </summary>
        private readonly IInboxLeaseStore _leaseStore;

        /// <summary>
        ///     The store role used to persist post-transition envelopes.
        /// </summary>
        private readonly IInboxStateWriter _stateWriter;

        /// <summary>
        ///     Initializes a new instance of the <see cref="SplitInboxProcessingStore" /> class.
        /// </summary>
        /// <param name="leaseStore">The store role used to lease due envelopes.</param>
        /// <param name="stateWriter">The store role used to persist post-transition envelopes.</param>
        public SplitInboxProcessingStore(IInboxLeaseStore leaseStore, IInboxStateWriter stateWriter)
        {
            _leaseStore = leaseStore ?? throw new ArgumentNullException(nameof(leaseStore));
            _stateWriter = stateWriter ?? throw new ArgumentNullException(nameof(stateWriter));
            _store = leaseStore as IInboxStore
                ?? stateWriter as IInboxStore
                ?? throw new ArgumentException(
                    "At least one store role must implement IInboxStore.",
                    nameof(leaseStore));
        }

        /// <inheritdoc />
        public Task<InboxEnvelope> AddAsync(InboxEnvelope envelope, CancellationToken cancellationToken = default) =>
            _store.AddAsync(envelope, cancellationToken);

        /// <inheritdoc />
        public Task<IReadOnlyList<InboxEnvelope>> AddBatchAsync(
            IReadOnlyList<InboxEnvelope> envelopes,
            CancellationToken cancellationToken = default) =>
            _store.AddBatchAsync(envelopes, cancellationToken);

        /// <inheritdoc />
        public Task<IReadOnlyList<InboxEnvelope>> LeasePendingAsync(
            InboxLeaseRequest request,
            CancellationToken cancellationToken = default) =>
            _leaseStore.LeasePendingAsync(request, cancellationToken);

        /// <inheritdoc />
        public Task<bool> RenewLeaseAsync(
            Guid messageId,
            string leaseOwner,
            DateTimeOffset expiresAt,
            CancellationToken cancellationToken = default) =>
            _leaseStore.RenewLeaseAsync(messageId, leaseOwner, expiresAt, cancellationToken);

        /// <inheritdoc />
        public Task PersistAsync(IReadOnlyList<InboxEnvelope> envelopes, CancellationToken cancellationToken = default) =>
            _stateWriter.PersistAsync(envelopes, cancellationToken);
    }
}
