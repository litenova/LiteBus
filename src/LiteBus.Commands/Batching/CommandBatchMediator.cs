using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using LiteBus.Commands.UnitOfWork;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Audit;
using LiteBus.Messaging.MediationStrategies;
using LiteBus.Messaging.Pipeline;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LiteBus.Commands.Batching;

/// <summary>
///     Sends several commands as one batch: they run one at a time in one dispatch scope, and the unit of work
///     commits once after all of them, or not at all.
/// </summary>
/// <remarks>
///     <para>
///         Every command runs its full pipeline through <see cref="IMessageMediator" /> with the batch's dispatch
///         scope set on the request, so all commands share the scoped unit of work, the transactional outbox and the
///         domain event collector. Commands never run in parallel, because a scoped database context is not
///         thread-safe.
///     </para>
///     <para>
///         A command counts as passed when its outcome is <see cref="MediationOutcome.Succeeded" /> or
///         <see cref="MediationOutcome.Answered" />. The first command that is denied, invalid, faulted or cancelled
///         stops the batch; later commands do not run, nothing is committed, and the unit of work is rolled back
///         when it implements <see cref="IUnitOfWorkBoundary" />.
///     </para>
///     <para>
///         The per-command unit-of-work completion handler does not commit inside a batch. This mediator calls
///         <see cref="IUnitOfWorkBoundary.BeginAsync" /> once before the first command, <see cref="IUnitOfWork.CommitAsync" />
///         once after all commands passed, and <see cref="IUnitOfWorkBoundary.RollbackAsync" /> when the batch stops
///         or the commit throws. Batch participants run before and after the single commit, and commit listeners run
///         once after it.
///     </para>
/// </remarks>
internal sealed class CommandBatchMediator : ICommandBatchMediator
{
    /// <summary>
    ///     Caches the batch send path per command runtime type, so the result-contract scan and the generic method
    ///     closure happen once per type rather than once per command.
    /// </summary>
    private static readonly ConcurrentDictionary<Type, Func<CommandBatchMediator, ICommand, CommandMediationSettings, IMessageDispatchScope, AuditRecordBuffer?, CancellationToken, Task<BatchCommandOutcome>>> Dispatchers = new();

    /// <summary>
    ///     The open generic helper closed over each result-bearing command's declared result type for the batch path.
    /// </summary>
    private static readonly MethodInfo ResultBearingMethod = typeof(CommandBatchMediator).GetMethod(nameof(ExecuteResultBearingAsync), BindingFlags.NonPublic | BindingFlags.Static) ?? throw new InvalidOperationException($"Could not resolve {nameof(ExecuteResultBearingAsync)}.");

    /// <summary>
    ///     The meter used for command batch instruments, sharing the mediation meter name so no OpenTelemetry
    ///     package change is needed.
    /// </summary>
    private static readonly Meter BatchMeter = new(LiteBusMediationTelemetry.MeterName);

    /// <summary>
    ///     The histogram of command batch duration in seconds.
    /// </summary>
    private static readonly Histogram<double> BatchDurationHistogram = BatchMeter.CreateHistogram<double>(LiteBusMediationTelemetry.BatchDurationInstrumentName, "s");

    /// <summary>
    ///     The histogram of the number of commands in one command batch.
    /// </summary>
    private static readonly Histogram<int> BatchSizeHistogram = BatchMeter.CreateHistogram<int>(LiteBusMediationTelemetry.BatchSizeInstrumentName);

    /// <summary>
    ///     The delegate that writes a batch commit listener failure event.
    /// </summary>
    private static readonly Action<ILogger, string, Exception> CommitListenerFailedMessage =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(2321, nameof(LogCommitListenerFailed)),
            "Commit listener {ListenerType} threw after the command batch committed. The data is committed; the listener fault is logged and the batch still succeeds.");

    /// <summary>
    ///     The delegate that writes a batch rollback failure event.
    /// </summary>
    private static readonly Action<ILogger, Exception?> RollbackFailedMessage =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(2322, nameof(LogRollbackFailed)),
            "The command batch rollback threw. The staged writes may not have been discarded.");

    /// <summary>
    ///     The delegate that writes a batch participant failure event after the commit.
    /// </summary>
    private static readonly Action<ILogger, string, Exception> AfterCommitFailedMessage =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(2323, nameof(LogAfterCommitFailed)),
            "Batch participant {ParticipantType} threw after the command batch committed. The data is committed; the fault is logged and the batch still succeeds.");

    /// <summary>
    ///     The delegate that writes a batch participant failure event during the rollback.
    /// </summary>
    private static readonly Action<ILogger, string, Exception> RollbackParticipantFailedMessage =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(2324, nameof(LogRollbackParticipantFailed)),
            "Batch participant {ParticipantType} threw during the command batch rollback. The fault is logged and the rollback continues with the remaining participants.");

    /// <summary>
    ///     Gets the core message mediator used to execute each command pipeline.
    /// </summary>
    private readonly IMessageMediator _messageMediator;

    /// <summary>
    ///     Gets the factory that creates the one dispatch scope shared by every command in the batch.
    /// </summary>
    private readonly IMessageDispatchScopeFactory _scopeFactory;

    /// <summary>
    ///     Gets the command module's unit-of-work registration, or <see langword="null" /> when no unit of work is registered.
    /// </summary>
    private readonly CommandUnitOfWorkRegistration? _unitOfWorkRegistration;

    /// <summary>
    ///     Records batch commit listener faults and rollback faults that must not fail the batch.
    /// </summary>
    private readonly ILogger<CommandBatchMediator> _logger;

    /// <summary>
    ///     Initializes a new instance of the <see cref="CommandBatchMediator" /> class.
    /// </summary>
    /// <param name="messageMediator">The core message mediator used to execute each command pipeline.</param>
    /// <param name="scopeFactory">The factory that creates the one dispatch scope shared by every command in the batch.</param>
    /// <param name="unitOfWorkRegistration">The command module's unit-of-work registration, or <see langword="null" /> when no unit of work is registered.</param>
    /// <param name="logger">Records batch listener and rollback faults, or <see langword="null" /> to discard them.</param>
    public CommandBatchMediator(
        IMessageMediator messageMediator,
        IMessageDispatchScopeFactory scopeFactory,
        CommandUnitOfWorkRegistration? unitOfWorkRegistration = null,
        ILogger<CommandBatchMediator>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(messageMediator);
        ArgumentNullException.ThrowIfNull(scopeFactory);

        _messageMediator = messageMediator;
        _scopeFactory = scopeFactory;
        _unitOfWorkRegistration = unitOfWorkRegistration;
        _logger = logger ?? NullLogger<CommandBatchMediator>.Instance;
    }

    /// <inheritdoc />
    public async Task SendManyAsync(
        IReadOnlyList<ICommand> commands,
        CommandBatchSettings? commandBatchSettings = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commands);

        await ExecuteBatchAsync(commands, commandBatchSettings, throwOnRefusal: true, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<CommandBatchResult> TrySendManyAsync(
        IReadOnlyList<ICommand> commands,
        CommandBatchSettings? commandBatchSettings = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commands);

        return ExecuteBatchAsync(commands, commandBatchSettings, throwOnRefusal: false, cancellationToken);
    }

    /// <summary>
    ///     Runs every command in one dispatch scope and commits once, or rolls back on the first failure.
    /// </summary>
    /// <param name="commands">The commands, run in list order.</param>
    /// <param name="commandBatchSettings">The settings applied to every command, or <see langword="null" />.</param>
    /// <param name="throwOnRefusal">Whether a refusal stops with the refusal exception rather than a rolled-back result.</param>
    /// <param name="cancellationToken">The token checked before each command.</param>
    /// <returns>The outcome of the batch and of each command.</returns>
    /// <remarks>
    ///     An empty list returns <see cref="CommandBatchOutcome.Committed" /> with no entries, opens no scope and
    ///     calls no unit-of-work method. A fault, a cancellation or a failed commit always propagates as an exception
    ///     after the rollback steps have run; only a refusal returns a result, and only when
    ///     <paramref name="throwOnRefusal" /> is <see langword="false" />.
    /// </remarks>
    private async Task<CommandBatchResult> ExecuteBatchAsync(
        IReadOnlyList<ICommand> commands,
        CommandBatchSettings? commandBatchSettings,
        bool throwOnRefusal,
        CancellationToken cancellationToken)
    {
        for (var index = 0; index < commands.Count; index++)
        {
            if (commands[index] is null)
            {
                throw new ArgumentException($"The command at index {index} must not be null.", nameof(commands));
            }
        }

        using var nesting = CommandBatchNesting.Enter();

        if (commands.Count == 0)
        {
            return new CommandBatchResult
            {
                Outcome = CommandBatchOutcome.Committed,
                Entries = [],
                FailedIndex = null
            };
        }

        commandBatchSettings ??= new CommandBatchSettings();
        var mediation = commandBatchSettings.Mediation ?? new CommandMediationSettings();
        var batchId = Guid.NewGuid().ToString();

        var scope = _scopeFactory.CreateScope();
        Activity? batchActivity = null;

        try
        {
            var batchContext = scope.ServiceProvider.GetService(typeof(CommandBatchContext)) as CommandBatchContext
                ?? throw new LiteBusDependencyResolutionException(typeof(CommandBatchContext));
            batchContext.Begin(batchId);
            var buffer = batchContext.AuditRecords;

            IUnitOfWork? unitOfWork = null;
            IUnitOfWorkBoundary? boundary = null;

            if (_unitOfWorkRegistration is not null)
            {
                var resolved = scope.ServiceProvider.GetService(_unitOfWorkRegistration.UnitOfWorkType) as IUnitOfWork
                    ?? throw new LiteBusDependencyResolutionException(_unitOfWorkRegistration.UnitOfWorkType);
                unitOfWork = resolved;
                boundary = resolved as IUnitOfWorkBoundary;
            }

            var resolvedParticipants = scope.ServiceProvider.GetService(typeof(IEnumerable<ICommandBatchParticipant>)) as IEnumerable<ICommandBatchParticipant>;
            IReadOnlyList<ICommandBatchParticipant> participants = resolvedParticipants is null ? [] : [.. resolvedParticipants];

            var resolvedListeners = scope.ServiceProvider.GetService(typeof(IEnumerable<IUnitOfWorkCommitListener>)) as IEnumerable<IUnitOfWorkCommitListener>;
            IReadOnlyList<IUnitOfWorkCommitListener> listeners = resolvedListeners is null ? [] : [.. resolvedListeners];

            var startedAt = Stopwatch.GetTimestamp();

            if (MediationTelemetry.Options.Spans)
            {
                batchActivity = MediationTelemetry.ActivitySource.StartActivity(LiteBusMediationTelemetry.BatchActivityName, ActivityKind.Internal);
            }

            var entries = new List<CommandBatchEntryResult>(commands.Count);
            var beginRan = false;
            var committed = false;
            var rollbackDone = false;
            var telemetryRecorded = false;

#pragma warning disable CA1031 // Every failure before the commit must roll back, record telemetry and still reach the caller.
            try
            {
                if (boundary is not null)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await boundary.BeginAsync(cancellationToken).ConfigureAwait(false);
                    beginRan = true;
                }

                for (var index = 0; index < commands.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var command = commands[index];
                    var dispatcher = Dispatchers.GetOrAdd(command.GetType(), CreateDispatcher);
                    var outcome = await dispatcher(this, command, mediation, scope, buffer, cancellationToken).ConfigureAwait(false);

                    entries.Add(new CommandBatchEntryResult(index, command.GetType(), true, outcome.Result));

                    if (outcome.Result.Outcome is MediationOutcome.Succeeded or MediationOutcome.Answered)
                    {
                        continue;
                    }

                    for (var rest = index + 1; rest < commands.Count; rest++)
                    {
                        entries.Add(new CommandBatchEntryResult(rest, commands[rest].GetType(), false, null));
                    }

                    if (beginRan && boundary is not null)
                    {
                        await TryRollbackUnitOfWorkAsync(boundary).ConfigureAwait(false);
                    }

                    await RollbackParticipantsAsync(participants).ConfigureAwait(false);
                    rollbackDone = true;

                    RecordBatchTelemetry(batchActivity, CommandBatchOutcome.RolledBack, commands.Count, Stopwatch.GetElapsedTime(startedAt), isError: false);
                    telemetryRecorded = true;

                    if (throwOnRefusal)
                    {
                        throw outcome.RefusalException ?? CreateRefusalException(command.GetType(), outcome.Result);
                    }

                    return new CommandBatchResult
                    {
                        Outcome = CommandBatchOutcome.RolledBack,
                        Entries = entries,
                        FailedIndex = index
                    };
                }

                foreach (var participant in participants)
                {
                    await participant.BeforeCommitAsync(CancellationToken.None).ConfigureAwait(false);
                }

                if (unitOfWork is not null)
                {
                    await unitOfWork.CommitAsync(CancellationToken.None).ConfigureAwait(false);
                }

                foreach (var participant in participants)
                {
                    try
                    {
                        await participant.AfterCommitAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        LogAfterCommitFailed(participant.GetType().FullName ?? participant.GetType().Name, exception);
                    }
                }

                foreach (var listener in listeners)
                {
                    try
                    {
                        await listener.OnCommittedAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        LogCommitListenerFailed(listener.GetType().FullName ?? listener.GetType().Name, exception);
                    }
                }

                committed = true;
                RecordBatchTelemetry(batchActivity, CommandBatchOutcome.Committed, commands.Count, Stopwatch.GetElapsedTime(startedAt), isError: false);
                telemetryRecorded = true;

                return new CommandBatchResult
                {
                    Outcome = CommandBatchOutcome.Committed,
                    Entries = entries,
                    FailedIndex = null
                };
            }
            catch (Exception exception)
            {
                if (!rollbackDone && !committed)
                {
                    if (beginRan && boundary is not null)
                    {
                        await TryRollbackUnitOfWorkAsync(boundary).ConfigureAwait(false);
                    }

                    await RollbackParticipantsAsync(participants).ConfigureAwait(false);
                    rollbackDone = true;
                }

                if (!telemetryRecorded)
                {
                    var isError = exception is not OperationCanceledException && !MediationExceptionFilters.IsRefusal(exception);
                    RecordBatchTelemetry(batchActivity, CommandBatchOutcome.RolledBack, commands.Count, Stopwatch.GetElapsedTime(startedAt), isError);
                    telemetryRecorded = true;
                }

                throw;
            }
#pragma warning restore CA1031
        }
        finally
        {
            batchActivity?.Dispose();
            await scope.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Runs one command that declares no single result type through the batch scope.
    /// </summary>
    /// <param name="command">The command to run.</param>
    /// <param name="commandMediationSettings">The settings applied to the command.</param>
    /// <param name="scope">The dispatch scope the batch shares across its commands.</param>
    /// <param name="buffer">The audit buffer the writer adds records to, or <see langword="null" /> outside a batch.</param>
    /// <param name="cancellationToken">The token cancelling the command.</param>
    /// <returns>The command outcome and the refusal to rethrow when the outcome stopped the batch.</returns>
    private async Task<BatchCommandOutcome> ExecuteWithoutResultAsync(
        ICommand command,
        CommandMediationSettings commandMediationSettings,
        IMessageDispatchScope scope,
        AuditRecordBuffer? buffer,
        CancellationToken cancellationToken)
    {
        var capture = new MediationEndingCapture();

        var request = new MessageMediationRequest<ICommand, Task>
        {
            MessageMediationStrategy = new SingleAsyncHandlerMediationStrategy<ICommand>(),
            MessageResolveStrategy = new ActualTypeOrFirstAssignableTypeMessageResolveStrategy(),
            Tags = commandMediationSettings.Routing.Tags,
            Items = CopyItems(commandMediationSettings.Items, capture, buffer),
            HandlerPredicate = commandMediationSettings.Routing.HandlerPredicate,
            DispatchScope = scope
        };

        try
        {
            await _messageMediator.Mediate(command, request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (MediationExceptionFilters.IsRefusal(exception))
        {
            EscapedRefusal.Record(capture, exception);
            return new BatchCommandOutcome(MediationResultFactory.FromCapture(capture), exception);
        }

        var result = MediationResultFactory.FromCapture(capture);

        if (result.Outcome is MediationOutcome.Denied or MediationOutcome.Invalid)
        {
            return new BatchCommandOutcome(result, CreateRefusalException(command.GetType(), capture));
        }

        return new BatchCommandOutcome(result, null);
    }

    /// <summary>
    ///     Runs one result-bearing command through the batch scope and drops the value the batch does not return.
    /// </summary>
    /// <typeparam name="TCommandResult">The result type the command declares.</typeparam>
    /// <param name="mediator">The mediator running the batch.</param>
    /// <param name="command">The command to run.</param>
    /// <param name="commandMediationSettings">The settings applied to the command.</param>
    /// <param name="scope">The dispatch scope the batch shares across its commands.</param>
    /// <param name="buffer">The audit buffer the writer adds records to, or <see langword="null" /> outside a batch.</param>
    /// <param name="cancellationToken">The token cancelling the command.</param>
    /// <returns>The command outcome and the refusal to rethrow when the outcome stopped the batch.</returns>
    private static async Task<BatchCommandOutcome> ExecuteResultBearingAsync<TCommandResult>(
        CommandBatchMediator mediator,
        ICommand command,
        CommandMediationSettings commandMediationSettings,
        IMessageDispatchScope scope,
        AuditRecordBuffer? buffer,
        CancellationToken cancellationToken)
    {
        var typedCommand = (ICommand<TCommandResult>) command;
        var capture = new MediationEndingCapture();

        var request = new MessageMediationRequest<ICommand<TCommandResult>, Task<TCommandResult>>
        {
            MessageMediationStrategy = new SingleAsyncHandlerMediationStrategy<ICommand<TCommandResult>, TCommandResult>(),
            MessageResolveStrategy = new ActualTypeOrFirstAssignableTypeMessageResolveStrategy(),
            Tags = commandMediationSettings.Routing.Tags,
            Items = CopyItems(commandMediationSettings.Items, capture, buffer),
            HandlerPredicate = commandMediationSettings.Routing.HandlerPredicate,
            DispatchScope = scope
        };

        try
        {
            var value = await mediator._messageMediator.Mediate(typedCommand, request, cancellationToken).ConfigureAwait(false);
            var typed = MediationResultFactory.FromCapture(capture, value, hasValue: true);
            var result = MapResultToNonGeneric(typed);

            if (result.Outcome is MediationOutcome.Denied or MediationOutcome.Invalid)
            {
                return new BatchCommandOutcome(result, CreateRefusalException(command.GetType(), capture));
            }

            return new BatchCommandOutcome(result, null);
        }
        catch (Exception exception) when (MediationExceptionFilters.IsRefusal(exception))
        {
            EscapedRefusal.Record(capture, exception);
            var typed = MediationResultFactory.FromCapture<TCommandResult>(capture, value: default, hasValue: false);
            return new BatchCommandOutcome(MapResultToNonGeneric(typed), exception);
        }
    }

    /// <summary>
    ///     Drops the value from a result-bearing outcome, keeping the ending the batch branches on.
    /// </summary>
    /// <typeparam name="TCommandResult">The result type the command declares.</typeparam>
    /// <param name="typed">The typed outcome the mediation reported.</param>
    /// <returns>The outcome without the value.</returns>
    private static MediationResult MapResultToNonGeneric<TCommandResult>(MediationResult<TCommandResult> typed)
    {
        return typed.Outcome switch
        {
            MediationOutcome.Denied => MediationResult.Denied(typed.Reason, typed.Code),
            MediationOutcome.Invalid => MediationResult.Invalid(typed.Reason, typed.Code, typed.Failures),
            MediationOutcome.Answered => MediationResult.Answered(typed.Reason, typed.Code),
            _ => MediationResult.Succeeded(),
        };
    }

    /// <summary>
    ///     Builds the batch send delegate for one command runtime type.
    /// </summary>
    /// <param name="commandType">The command runtime type the delegate is built for.</param>
    /// <returns>The delegate that runs commands of that type inside the batch scope.</returns>
    /// <remarks>
    ///     A command whose runtime type implements several <see cref="ICommand{TCommandResult}" /> contracts keeps
    ///     the no-result path: there is no single result type to close the generic overload over, so the pipeline
    ///     runs without a result rather than guessing which contract the caller meant.
    /// </remarks>
    private static Func<CommandBatchMediator, ICommand, CommandMediationSettings, IMessageDispatchScope, AuditRecordBuffer?, CancellationToken, Task<BatchCommandOutcome>> CreateDispatcher(Type commandType)
    {
        var resultType = FindSingleResultType(commandType);

        if (resultType is null)
        {
            return static (mediator, command, settings, scope, buffer, cancellationToken) => mediator.ExecuteWithoutResultAsync(command, settings, scope, buffer, cancellationToken);
        }

        return ResultBearingMethod.MakeGenericMethod(resultType).CreateDelegate<Func<CommandBatchMediator, ICommand, CommandMediationSettings, IMessageDispatchScope, AuditRecordBuffer?, CancellationToken, Task<BatchCommandOutcome>>>();
    }

    /// <summary>
    ///     Finds the single result type a command runtime type declares through <see cref="ICommand{TCommandResult}" />.
    /// </summary>
    /// <param name="commandType">The command runtime type to inspect.</param>
    /// <returns>The declared result type, or <see langword="null" /> when the command declares none or several.</returns>
    private static Type? FindSingleResultType(Type commandType)
    {
        Type? resultType = null;

        foreach (var contract in commandType.GetInterfaces())
        {
            if (!contract.IsGenericType || contract.GetGenericTypeDefinition() != typeof(ICommand<>))
            {
                continue;
            }

            if (resultType is not null)
            {
                return null;
            }

            resultType = contract.GetGenericArguments()[0];
        }

        return resultType;
    }

    /// <summary>
    ///     Copies the caller's items and adds the ending capture and the audit buffer for one command.
    /// </summary>
    /// <param name="items">The items the caller supplied.</param>
    /// <param name="capture">The capture the strategy fills in.</param>
    /// <param name="buffer">The audit buffer the writer adds records to, or <see langword="null" /> to add none.</param>
    /// <returns>The items to pass to the mediator.</returns>
    /// <remarks>
    ///     Copied rather than mutated, because the settings object is shared across every command in the batch and
    ///     installing one command's capture into it would leave the next command writing into the previous command's
    ///     result.
    /// </remarks>
    private static Dictionary<string, object> CopyItems(
        IDictionary<string, object> items,
        MediationEndingCapture capture,
        AuditRecordBuffer? buffer)
    {
        var copy = new Dictionary<string, object>(items, StringComparer.Ordinal)
        {
            [MediationEndingCapture.ItemKey] = capture
        };

        if (buffer is not null)
        {
            copy[AuditRecordBuffer.ItemKey] = buffer;
        }

        return copy;
    }

    /// <summary>
    ///     Builds the refusal exception for a mapped denial or validation failure the pipeline answered with a value.
    /// </summary>
    /// <param name="commandType">The runtime type of the command that was refused.</param>
    /// <param name="capture">The ending the mediation strategy recorded.</param>
    /// <returns>The exception <see cref="SendManyAsync" /> throws for the refused command.</returns>
    private static Exception CreateRefusalException(Type commandType, MediationEndingCapture capture)
    {
        if (capture.Outcome == MediationOutcome.Invalid)
        {
            return new LiteBusMessageInvalidException(commandType, capture.Failures, capture.DecidedBy);
        }

        return new LiteBusMessageDeniedException(commandType, capture.Reason ?? "no reason was given", capture.Code, capture.DecidedBy);
    }

    /// <summary>
    ///     Builds the refusal exception for a denial or validation failure reported without its exception.
    /// </summary>
    /// <param name="commandType">The runtime type of the command that was refused.</param>
    /// <param name="result">The outcome the command reported.</param>
    /// <returns>The exception <see cref="SendManyAsync" /> throws for the refused command.</returns>
    private static Exception CreateRefusalException(Type commandType, MediationResult result)
    {
        if (result.Outcome == MediationOutcome.Invalid)
        {
            return new LiteBusMessageInvalidException(commandType, result.Failures);
        }

        return new LiteBusMessageDeniedException(commandType, result.Reason ?? "no reason was given", result.Code);
    }

    /// <summary>
    ///     Rolls back the unit-of-work transaction without replacing the failure that stopped the batch.
    /// </summary>
    /// <param name="boundary">The unit-of-work boundary to roll back.</param>
    /// <returns>A task representing the asynchronous rollback.</returns>
    /// <remarks>
    ///     A rollback that throws is logged and never replaces the original failure: the caller must see the
    ///     refusal, the fault or the commit exception that stopped the batch, not the rollback fault.
    /// </remarks>
    private async Task TryRollbackUnitOfWorkAsync(IUnitOfWorkBoundary boundary)
    {
#pragma warning disable CA1031 // A rollback fault must be recorded without replacing the failure that stopped the batch.
        try
        {
            await boundary.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            LogRollbackFailed(exception);
        }
#pragma warning restore CA1031
    }

    /// <summary>
    ///     Rolls back every batch participant, continuing past one that throws.
    /// </summary>
    /// <param name="participants">The participants to roll back, in registration order.</param>
    /// <returns>A task representing the asynchronous rollback.</returns>
    /// <remarks>
    ///     A participant that throws is logged and the remaining participants still run, so one failing release or
    ///     audit write cannot skip the others.
    /// </remarks>
    private async Task RollbackParticipantsAsync(IReadOnlyList<ICommandBatchParticipant> participants)
    {
        foreach (var participant in participants)
        {
#pragma warning disable CA1031 // A participant rollback fault is logged so the remaining participants still run.
            try
            {
                await participant.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                LogRollbackParticipantFailed(participant.GetType().FullName ?? participant.GetType().Name, exception);
            }
#pragma warning restore CA1031
        }
    }

    /// <summary>
    ///     Records how one batch ended and annotates its span.
    /// </summary>
    /// <param name="activity">The batch span, when one was started.</param>
    /// <param name="outcome">How the batch ended.</param>
    /// <param name="batchSize">The number of commands in the batch.</param>
    /// <param name="elapsed">How long the batch took.</param>
    /// <param name="isError">Whether the batch ended with a fault rather than a decision.</param>
    private static void RecordBatchTelemetry(
        Activity? activity,
        CommandBatchOutcome outcome,
        int batchSize,
        TimeSpan elapsed,
        bool isError)
    {
        var outcomeValue = outcome == CommandBatchOutcome.Committed ? "committed" : "rolled_back";

        if (activity is not null)
        {
            activity.SetTag(LiteBusMediationTelemetry.BatchOutcomeAttributeName, outcomeValue);
            activity.SetStatus(isError ? ActivityStatusCode.Error : ActivityStatusCode.Ok);
        }

        if (!MediationTelemetry.Options.Metrics)
        {
            return;
        }

        BatchDurationHistogram.Record(
            elapsed.TotalSeconds,
            new KeyValuePair<string, object?>(LiteBusMediationTelemetry.BatchOutcomeAttributeName, outcomeValue));
        BatchSizeHistogram.Record(batchSize);
    }

    /// <summary>
    ///     Writes a batch commit listener failure event.
    /// </summary>
    /// <param name="listenerType">The full name of the listener type that threw.</param>
    /// <param name="exception">The exception the listener threw.</param>
    private void LogCommitListenerFailed(string listenerType, Exception exception)
    {
        CommitListenerFailedMessage(_logger, listenerType, exception);
    }

    /// <summary>
    ///     Writes a batch rollback failure event.
    /// </summary>
    /// <param name="exception">The exception the rollback threw.</param>
    private void LogRollbackFailed(Exception exception)
    {
        RollbackFailedMessage(_logger, exception);
    }

    /// <summary>
    ///     Writes a batch participant failure event after the commit.
    /// </summary>
    /// <param name="participantType">The full name of the participant type that threw.</param>
    /// <param name="exception">The exception the participant threw.</param>
    private void LogAfterCommitFailed(string participantType, Exception exception)
    {
        AfterCommitFailedMessage(_logger, participantType, exception);
    }

    /// <summary>
    ///     Writes a batch participant failure event during the rollback.
    /// </summary>
    /// <param name="participantType">The full name of the participant type that threw.</param>
    /// <param name="exception">The exception the participant threw.</param>
    private void LogRollbackParticipantFailed(string participantType, Exception exception)
    {
        RollbackParticipantFailedMessage(_logger, participantType, exception);
    }

    /// <summary>
    ///     Reports how one command of a batch ended and what to rethrow when it stopped the batch.
    /// </summary>
    /// <param name="Result">The command mediation result.</param>
    /// <param name="RefusalException">The refusal to rethrow, or <see langword="null" /> when the command passed.</param>
    private sealed record BatchCommandOutcome(MediationResult Result, Exception? RefusalException);
}
