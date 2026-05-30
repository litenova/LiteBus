using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using LiteBus.Inbox.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Inbox.Dispatch.Commands;

/// <summary>
///     Dispatches inbox envelopes through the LiteBus in-process command mediator.
/// </summary>
/// <remarks>
///     <para>
///         This dispatcher resolves the CLR type from the stored contract, deserializes the payload, and calls
///         <see cref="ICommandMediator.SendAsync(ICommand, CommandMediationSettings?, CancellationToken)" />. It sets
///         <see cref="InboxExecutionContextKeys.IsInboxExecution" /> on the mediation settings so pre-handlers,
///         post-handlers, and error handlers can detect inbox replay.
///     </para>
///     <para>
///         Register a custom <see cref="IInboxDispatcher" /> when the inbox needs to execute messages through a
///         different pipeline.
///     </para>
/// </remarks>
public sealed class CommandInboxDispatcher : IInboxDispatcher
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
    ///     Initializes a new instance of the <see cref="CommandInboxDispatcher" /> class.
    /// </summary>
    /// <param name="commandMediator">The command mediator used to execute deserialized commands.</param>
    /// <param name="contractRegistry">The registry used to resolve persisted contracts back to CLR types.</param>
    /// <param name="messageSerializer">The serializer used to hydrate envelope payloads.</param>
    public CommandInboxDispatcher(
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
