using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using LiteBus.Inbox.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Inbox;

/// <summary>
///     Default processor that leases inbox commands and executes them through LiteBus command mediation.
/// </summary>
/// <remarks>
///     <para>
///         Each processing pass leases a bounded batch, resolves each stored contract back to its CLR command type,
///         deserializes the payload, then calls <see cref="ICommandMediator.SendAsync(ICommand, CommandMediationSettings?, CancellationToken)" />.
///     </para>
///     <para>
///         Failures are recorded through <see cref="ICommandInboxStateStore" />. Retry timing is calculated here so store
///         implementations do not need retry-policy knowledge. Cancellation is allowed to escape so the host can stop
///         without converting shutdown into a command failure.
///     </para>
/// </remarks>
public sealed class CommandInboxProcessor : Abstractions.ICommandInboxProcessor
{
    /// <summary>
    ///     Gets the time provider used for leasing and retry timestamps.
    /// </summary>
    private readonly TimeProvider _clock;

    /// <summary>
    ///     Gets the command mediator used to execute leased commands.
    /// </summary>
    private readonly ICommandMediator _commandMediator;

    /// <summary>
    ///     Gets the registry used to resolve persisted contracts back to command types.
    /// </summary>
    private readonly IMessageContractRegistry _contractRegistry;

    /// <summary>
    ///     Gets the lease owner name assigned to commands claimed by this processor instance.
    /// </summary>
    private readonly string _leaseOwner;

    /// <summary>
    ///     Gets the serializer used to hydrate leased command payloads.
    /// </summary>
    private readonly IMessageSerializer _messageSerializer;

    /// <summary>
    ///     Gets the batch, lease, owner, and retry settings for this processor instance.
    /// </summary>
    private readonly CommandInboxProcessorOptions _options;

    /// <summary>
    ///     Gets the store role used to lease due commands.
    /// </summary>
    private readonly ICommandInboxLeaseStore _leaseStore;

    /// <summary>
    ///     Gets the store role used to record command execution results.
    /// </summary>
    private readonly ICommandInboxStateStore _stateStore;

    /// <summary>
    ///     Initializes a new instance of the <see cref="CommandInboxProcessor" /> class.
    /// </summary>
    /// <param name="leaseStore">The store role used to lease due commands.</param>
    /// <param name="stateStore">The store role used to record command execution results.</param>
    /// <param name="commandMediator">The command mediator used to execute leased commands.</param>
    /// <param name="contractRegistry">The registry used to resolve persisted contracts back to command types.</param>
    /// <param name="messageSerializer">The serializer used to hydrate leased payloads.</param>
    /// <param name="options">The batch, lease, owner, and retry settings for this processor instance.</param>
    /// <param name="clock">The time provider used for leasing and retry timestamps.</param>
    public CommandInboxProcessor(
        ICommandInboxLeaseStore leaseStore,
        ICommandInboxStateStore stateStore,
        ICommandMediator commandMediator,
        IMessageContractRegistry contractRegistry,
        IMessageSerializer messageSerializer,
        CommandInboxProcessorOptions options,
        TimeProvider clock)
    {
        _leaseStore = leaseStore ?? throw new ArgumentNullException(nameof(leaseStore));
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _commandMediator = commandMediator ?? throw new ArgumentNullException(nameof(commandMediator));
        _contractRegistry = contractRegistry ?? throw new ArgumentNullException(nameof(contractRegistry));
        _messageSerializer = messageSerializer ?? throw new ArgumentNullException(nameof(messageSerializer));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _leaseOwner = string.IsNullOrWhiteSpace(_options.LeaseOwner)
            ? $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}"
            : _options.LeaseOwner;

        if (_options.BatchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), _options.BatchSize, "Batch size must be greater than zero.");
        }

        if (_options.LeaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), _options.LeaseDuration, "Lease duration must be greater than zero.");
        }

        MessageProcessorDiagnostics.ValidateRetryOptions(_options.Retry, nameof(options));
    }

    /// <inheritdoc />
    public async Task<ProcessorPassResult> ProcessPendingAsync(CancellationToken cancellationToken = default)
    {
        var now = _clock.GetUtcNow();
        var leasedCommands = await _leaseStore.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = _options.BatchSize,
            LeaseOwner = _leaseOwner,
            Now = now,
            LeaseDuration = _options.LeaseDuration
        }, cancellationToken).ConfigureAwait(false);

        foreach (var envelope in leasedCommands)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ProcessCommandAsync(envelope, cancellationToken).ConfigureAwait(false);
        }

        return new ProcessorPassResult
        {
            LeasedCount = leasedCommands.Count
        };
    }

    /// <summary>
    ///     Executes one leased command envelope and records its terminal state for this attempt.
    /// </summary>
    /// <param name="envelope">The leased command envelope returned by the store.</param>
    /// <param name="cancellationToken">A token used to cancel deserialization or command mediation.</param>
    /// <returns>A task that represents the asynchronous command execution and state update.</returns>
    private async Task ProcessCommandAsync(InboxCommandEnvelope envelope, CancellationToken cancellationToken)
    {
        try
        {
            var commandType = _contractRegistry.GetMessageType(envelope.ContractName, envelope.ContractVersion);
            var message = await _messageSerializer.DeserializeAsync(commandType, envelope.Payload, cancellationToken).ConfigureAwait(false);

            if (message is not ICommand command)
            {
                throw new InvalidOperationException($"Contract '{envelope.ContractName}' version {envelope.ContractVersion} resolved to a type that does not implement ICommand.");
            }

            var mediationSettings = new CommandMediationSettings();
            mediationSettings.Items[CommandInboxExecutionContextKeys.IsInboxExecution] = true;
            MessageProcessorDiagnostics.ApplyTraceMetadata(
                mediationSettings.Items,
                envelope.CorrelationId,
                envelope.CausationId,
                envelope.TenantId);

            await _commandMediator.SendAsync(command, mediationSettings, cancellationToken).ConfigureAwait(false);
            await _stateStore.MarkCompletedAsync(envelope.CommandId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await MarkFailedAsync(envelope, exception, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Converts a handler, deserialization, or dispatch failure into retry or dead-letter state.
    /// </summary>
    /// <param name="envelope">The command envelope that failed during this attempt.</param>
    /// <param name="exception">The exception captured from command execution.</param>
    /// <param name="cancellationToken">A token used to cancel the state update.</param>
    /// <returns>A task that represents the asynchronous retry or dead-letter state update.</returns>
    private Task MarkFailedAsync(InboxCommandEnvelope envelope, Exception exception, CancellationToken cancellationToken)
    {
        var error = MessageProcessorDiagnostics.FormatError(exception);

        if (envelope.AttemptCount >= _options.Retry.MaxAttempts)
        {
            return _stateStore.MoveToDeadLetterAsync(new InboxCommandDeadLetter
            {
                CommandId = envelope.CommandId,
                Reason = error
            }, cancellationToken);
        }

        var visibleAfter = _clock.GetUtcNow().Add(CalculateRetryDelay(envelope.AttemptCount));

        return _stateStore.MarkFailedAsync(new InboxCommandFailure
        {
            CommandId = envelope.CommandId,
            Error = error,
            VisibleAfter = visibleAfter
        }, cancellationToken);
    }

    /// <summary>
    ///     Calculates the next retry delay from the attempt count already recorded by the leasing operation.
    /// </summary>
    /// <param name="attemptCount">The current persisted attempt count for the command.</param>
    /// <returns>The delay to add to the current clock value before the command becomes visible again.</returns>
    private TimeSpan CalculateRetryDelay(int attemptCount)
    {
        var retryOptions = _options.Retry;
        var initialDelayTicks = retryOptions.InitialDelay.Ticks;
        var delayTicks = retryOptions.Backoff == RetryBackoff.Fixed
            ? initialDelayTicks
            : initialDelayTicks * Math.Pow(2, Math.Max(0, attemptCount - 1));

        var delay = TimeSpan.FromTicks((long)Math.Min(delayTicks, retryOptions.MaxDelay.Ticks));

        if (!retryOptions.UseJitter || delay == TimeSpan.Zero)
        {
            return delay;
        }

        var jitterFactor = 0.8 + Random.Shared.NextDouble() * 0.4;
        return TimeSpan.FromTicks((long)Math.Min(delay.Ticks * jitterFactor, retryOptions.MaxDelay.Ticks));
    }
}