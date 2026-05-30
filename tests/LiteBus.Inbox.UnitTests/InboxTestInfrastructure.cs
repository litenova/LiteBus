using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using LiteBus.Inbox.Abstractions;
using LiteBus.Messaging.Abstractions;
using Microsoft.Extensions.DependencyInjection;

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

    internal sealed class ThrowingInboxLeaseStore : IInboxLeaseStore
    {
        private readonly int _failuresBeforeSuccess;
        private int _attempts;
        private readonly CommandInboxTests.InMemoryCommandInboxStore _inner = new();

        public ThrowingInboxLeaseStore(int failuresBeforeSuccess = int.MaxValue)
        {
            _failuresBeforeSuccess = failuresBeforeSuccess;
        }

        public CommandInboxTests.InMemoryCommandInboxStore Inner => _inner;

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
    }

    internal sealed class DelegatingInboxLeaseStore : IInboxLeaseStore
    {
        private readonly CommandInboxTests.InMemoryCommandInboxStore _inner;
        private readonly Func<InboxLeaseRequest, IReadOnlyList<InboxEnvelope>>? _onLease;

        public DelegatingInboxLeaseStore(
            CommandInboxTests.InMemoryCommandInboxStore inner,
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
}
