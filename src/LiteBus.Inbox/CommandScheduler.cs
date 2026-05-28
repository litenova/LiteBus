using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using LiteBus.Inbox.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Inbox;

/// <summary>
///     Default scheduler that turns a command instance into a inbox envelope.
/// </summary>
/// <remarks>
///     <para>
///         The scheduler performs only acceptance work: contract lookup, serialization, metadata mapping, and append to
///         the configured <see cref="ICommandInboxWriter" />. It never executes the command handler. Execution belongs to
///         <see cref="CommandInboxProcessor" />.
///     </para>
///     <para>
///         The runtime command type is used for contract lookup so closed generic command instances are stored with the
///         contract registered for that closed type.
///     </para>
/// </remarks>
public sealed class CommandScheduler : ICommandScheduler
{
    private readonly TimeProvider _clock;
    private readonly IMessageContractRegistry _contractRegistry;
    private readonly IMessageSerializer _messageSerializer;
    private readonly ICommandInboxWriter _store;

    /// <summary>
    ///     Initializes a new instance of the <see cref="CommandScheduler" /> class.
    /// </summary>
    /// <param name="store">The inbox writer store used to persist newly scheduled commands.</param>
    /// <param name="contractRegistry">The registry used to map the runtime command type to a stable contract.</param>
    /// <param name="messageSerializer">The serializer used to create the serialized payload.</param>
    /// <param name="clock">The time provider used to stamp acceptance time.</param>
    public CommandScheduler(
        ICommandInboxWriter store,
        IMessageContractRegistry contractRegistry,
        IMessageSerializer messageSerializer,
        TimeProvider clock)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _contractRegistry = contractRegistry ?? throw new ArgumentNullException(nameof(contractRegistry));
        _messageSerializer = messageSerializer ?? throw new ArgumentNullException(nameof(messageSerializer));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <inheritdoc />
    public async Task<CommandReceipt<TCommand>> ScheduleAsync<TCommand>(
        TCommand command,
        CommandScheduleOptions? options = null,
        CancellationToken cancellationToken = default)
        where TCommand : ICommand
    {
        ArgumentNullException.ThrowIfNull(command);

        options ??= new CommandScheduleOptions();

        var commandType = command.GetType();

        if (commandType.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommand<>)))
        {
            throw new ArgumentException(
                $"Commands that implement ICommand<TResult> cannot be scheduled for deferred execution because the handler result would be discarded. Type: '{commandType.Name}'.",
                nameof(command));
        }

        var contract = _contractRegistry.GetContract(commandType);
        var acceptedAt = _clock.GetUtcNow();
        var commandId = options.CommandId ?? Guid.NewGuid();
        var payload = await _messageSerializer.SerializeAsync(command, cancellationToken).ConfigureAwait(false);
        var idempotencyKey = options.IdempotencyKey;

        if (string.IsNullOrWhiteSpace(idempotencyKey) && command is IIdempotentCommand idempotentCommand)
        {
            idempotencyKey = idempotentCommand.IdempotencyKey;
        }

        var envelope = new InboxCommandEnvelope
        {
            CommandId = commandId,
            ContractName = contract.Name,
            ContractVersion = contract.Version,
            Payload = payload,
            CreatedAt = acceptedAt,
            VisibleAfter = options.VisibleAfter,
            AttemptCount = 0,
            Status = InboxCommandStatus.Pending,
            IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey,
            CorrelationId = options.CorrelationId,
            CausationId = options.CausationId,
            TenantId = options.TenantId
        };

        var storedEnvelope = await _store.AddAsync(envelope, cancellationToken).ConfigureAwait(false);

        return new CommandReceipt<TCommand>
        {
            CommandId = storedEnvelope.CommandId,
            CommandType = commandType,
            ContractName = storedEnvelope.ContractName,
            ContractVersion = storedEnvelope.ContractVersion,
            AcceptedAt = storedEnvelope.CreatedAt,
            CorrelationId = storedEnvelope.CorrelationId,
            CausationId = storedEnvelope.CausationId,
            TenantId = storedEnvelope.TenantId
        };
    }
}