using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Testing;

/// <summary>
///     Records command batches sent through <see cref="ICommandBatchMediator" /> for test assertions.
/// </summary>
/// <remarks>
///     All members are thread-safe. Recording properties return snapshots, so a held reference never observes later
///     batches and mutating it never affects the recorded history. Each entry of <see cref="Batches" /> corresponds
///     to the entry of <see cref="BatchSettings" /> at the same index.
/// </remarks>
public sealed class TestCommandBatchMediator : ICommandBatchMediator
{
    /// <summary>
    ///     Gets the command lists recorded by <see cref="SendManyAsync" /> and <see cref="TrySendManyAsync" />.
    /// </summary>
    private readonly List<IReadOnlyList<ICommand>> _batches = [];

    /// <summary>
    ///     Gets the settings recorded alongside each batch, at the same index.
    /// </summary>
    private readonly List<CommandBatchSettings?> _batchSettings = [];

    /// <summary>
    ///     Gets the lock that serializes recording mutations, snapshot reads, and stub consumption.
    /// </summary>
    private readonly object _sync = new();

    /// <summary>
    ///     The stubbed result consumed by the next batch.
    /// </summary>
    private CommandBatchResult? _nextResult;

    /// <summary>
    ///     The index at which batches are refused, or <see langword="null" /> when they are not refused.
    /// </summary>
    private int? _refusedIndex;

    /// <summary>
    ///     The mediation result reported at the refused index.
    /// </summary>
    private MediationResult _refusal = MediationResult.Denied("Refused by the test batch mediator.", code: null);

    /// <summary>
    ///     Gets a snapshot of the command lists recorded since construction or the last <see cref="Clear" /> call.
    /// </summary>
    /// <value>
    ///     One entry per <see cref="SendManyAsync" /> or <see cref="TrySendManyAsync" /> call, in call order. Each
    ///     entry is a copy taken when the call was recorded, so mutating the list the test passed never changes the
    ///     recorded history.
    /// </value>
    public IReadOnlyList<IReadOnlyList<ICommand>> Batches
    {
        get
        {
            lock (_sync)
            {
                return [.. _batches];
            }
        }
    }

    /// <summary>
    ///     Gets a snapshot of the settings recorded since construction or the last <see cref="Clear" /> call.
    /// </summary>
    /// <value>
    ///     One entry per call, in call order. The entry at an index is the settings the batch at the same index of
    ///     <see cref="Batches" /> was sent with, or <see langword="null" /> when the call passed none.
    /// </value>
    public IReadOnlyList<CommandBatchSettings?> BatchSettings
    {
        get
        {
            lock (_sync)
            {
                return [.. _batchSettings];
            }
        }
    }

    /// <summary>
    ///     Gets or sets the result returned for the next batch.
    /// </summary>
    /// <value>
    ///     The stub is single-use: each <see cref="SendManyAsync" /> and <see cref="TrySendManyAsync" /> call consumes
    ///     it and resets the property to <see langword="null" />, so a test that forgets to stub the second batch
    ///     observes the default result instead of a stale stub. When <see langword="null" />, the batch reports the
    ///     configured refusal when <see cref="RefusedIndex" /> falls inside it, and the default committed result
    ///     otherwise.
    /// </value>
    public CommandBatchResult? NextResult
    {
        get
        {
            lock (_sync)
            {
                return _nextResult;
            }
        }

        set
        {
            lock (_sync)
            {
                _nextResult = value;
            }
        }
    }

    /// <summary>
    ///     Gets or sets the index at which batches are refused.
    /// </summary>
    /// <value>
    ///     The index of the command that stops the batch, or <see langword="null" /> when batches are not refused.
    ///     Unlike <see cref="NextResult" />, the refusal stays configured until it is changed or <see cref="Clear" />
    ///     runs, so every batch sent while it is set stops at the same index. An index outside the sent list refuses
    ///     nothing.
    /// </value>
    public int? RefusedIndex
    {
        get
        {
            lock (_sync)
            {
                return _refusedIndex;
            }
        }

        set
        {
            if (value.HasValue)
            {
                ArgumentOutOfRangeException.ThrowIfNegative(value.Value);
            }

            lock (_sync)
            {
                _refusedIndex = value;
            }
        }
    }

    /// <summary>
    ///     Gets or sets the mediation result reported at <see cref="RefusedIndex" />.
    /// </summary>
    /// <value>
    ///     Reported as the entry result of the refused command, while commands after it are reported as not executed.
    /// </value>
    public MediationResult Refusal
    {
        get
        {
            lock (_sync)
            {
                return _refusal;
            }
        }

        set
        {
            lock (_sync)
            {
                _refusal = value;
            }
        }
    }

    /// <inheritdoc />
    public Task SendManyAsync(
        IReadOnlyList<ICommand> commands,
        CommandBatchSettings? commandBatchSettings = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ThrowIfAnyNull(commands);

        CommandBatchResult result;

        lock (_sync)
        {
            RecordLocked(commands, commandBatchSettings);
            result = ResolveLocked(commands);
        }

        if (result.Outcome == CommandBatchOutcome.RolledBack)
        {
            throw ToRefusalException(result, commands);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<CommandBatchResult> TrySendManyAsync(
        IReadOnlyList<ICommand> commands,
        CommandBatchSettings? commandBatchSettings = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ThrowIfAnyNull(commands);

        lock (_sync)
        {
            RecordLocked(commands, commandBatchSettings);
            return Task.FromResult(ResolveLocked(commands));
        }
    }

    /// <summary>
    ///     Configures the mediator to refuse the command at the given index.
    /// </summary>
    /// <param name="index">The index of the command that stops the batch.</param>
    /// <param name="refusal">The mediation result reported for the refused command.</param>
    public void RefuseAt(int index, MediationResult refusal)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);

        lock (_sync)
        {
            _refusedIndex = index;
            _refusal = refusal;
        }
    }

    /// <summary>
    ///     Clears recorded batches and resets the stubbed result and the configured refusal.
    /// </summary>
    public void Clear()
    {
        lock (_sync)
        {
            _batches.Clear();
            _batchSettings.Clear();
            _nextResult = null;
            _refusedIndex = null;
            _refusal = MediationResult.Denied("Refused by the test batch mediator.", code: null);
        }
    }

    /// <summary>
    ///     Throws when any command in the batch is <see langword="null" />.
    /// </summary>
    /// <param name="commands">The commands to check.</param>
    private static void ThrowIfAnyNull(IReadOnlyList<ICommand> commands)
    {
        for (var i = 0; i < commands.Count; i++)
        {
            if (commands[i] is null)
            {
                throw new ArgumentException($"The command at index {i} must not be null.", nameof(commands));
            }
        }
    }

    /// <summary>
    ///     Records one call's command list and settings.
    /// </summary>
    /// <param name="commands">The commands the call was sent with.</param>
    /// <param name="commandBatchSettings">The settings the call was sent with.</param>
    /// <remarks>
    ///     Callers must hold the mutation lock; the recording lists mutate in place.
    /// </remarks>
    private void RecordLocked(IReadOnlyList<ICommand> commands, CommandBatchSettings? commandBatchSettings)
    {
        _batches.Add([.. commands]);
        _batchSettings.Add(commandBatchSettings);
    }

    /// <summary>
    ///     Resolves the result reported for one batch.
    /// </summary>
    /// <param name="commands">The commands the batch was sent with.</param>
    /// <returns>
    ///     The stubbed <see cref="NextResult" /> when one is set; otherwise the configured refusal when
    ///     <see cref="RefusedIndex" /> falls inside the batch; otherwise a committed result whose entries all report
    ///     success.
    /// </returns>
    /// <remarks>
    ///     Callers must hold the mutation lock; the stub field is consumed in place.
    /// </remarks>
    private CommandBatchResult ResolveLocked(IReadOnlyList<ICommand> commands)
    {
        if (_nextResult is not null)
        {
            var stub = _nextResult;
            _nextResult = null;
            return stub;
        }

        if (_refusedIndex.HasValue && _refusedIndex.Value < commands.Count)
        {
            return BuildRefusedResult(commands, _refusedIndex.Value, _refusal);
        }

        return BuildCommittedResult(commands);
    }

    /// <summary>
    ///     Builds the default result, in which every command ran and succeeded.
    /// </summary>
    /// <param name="commands">The commands the batch was sent with.</param>
    /// <returns>A committed result with one executed entry per command.</returns>
    private static CommandBatchResult BuildCommittedResult(IReadOnlyList<ICommand> commands)
    {
        var entries = new List<CommandBatchEntryResult>(commands.Count);

        for (var i = 0; i < commands.Count; i++)
        {
            entries.Add(new CommandBatchEntryResult(i, commands[i].GetType(), true, MediationResult.Succeeded()));
        }

        return new CommandBatchResult
        {
            Outcome = CommandBatchOutcome.Committed,
            Entries = entries
        };
    }

    /// <summary>
    ///     Builds the result for a batch stopped by the configured refusal.
    /// </summary>
    /// <param name="commands">The commands the batch was sent with.</param>
    /// <param name="refusedIndex">The index of the command that stopped the batch.</param>
    /// <param name="refusal">The mediation result reported for the refused command.</param>
    /// <returns>
    ///     A rolled-back result whose entries before <paramref name="refusedIndex" /> report success, whose entry at
    ///     <paramref name="refusedIndex" /> reports <paramref name="refusal" />, and whose later entries report that
    ///     they did not run.
    /// </returns>
    private static CommandBatchResult BuildRefusedResult(
        IReadOnlyList<ICommand> commands,
        int refusedIndex,
        MediationResult refusal)
    {
        var entries = new List<CommandBatchEntryResult>(commands.Count);

        for (var i = 0; i < commands.Count; i++)
        {
            if (i < refusedIndex)
            {
                entries.Add(new CommandBatchEntryResult(i, commands[i].GetType(), true, MediationResult.Succeeded()));
            }
            else if (i == refusedIndex)
            {
                entries.Add(new CommandBatchEntryResult(i, commands[i].GetType(), true, refusal));
            }
            else
            {
                entries.Add(new CommandBatchEntryResult(i, commands[i].GetType(), false, null));
            }
        }

        return new CommandBatchResult
        {
            Outcome = CommandBatchOutcome.RolledBack,
            Entries = entries,
            FailedIndex = refusedIndex
        };
    }

    /// <summary>
    ///     Converts a rolled-back result into the exception <see cref="SendManyAsync" /> raises.
    /// </summary>
    /// <param name="result">The rolled-back result.</param>
    /// <param name="commands">The commands the batch was sent with.</param>
    /// <returns>
    ///     The denial or validation exception the failing command's mediation result describes, or an
    ///     <see cref="InvalidOperationException" /> when the result names no refusal.
    /// </returns>
    private static Exception ToRefusalException(CommandBatchResult result, IReadOnlyList<ICommand> commands)
    {
        if (result.FailedIndex.HasValue
            && result.FailedIndex.Value >= 0
            && result.FailedIndex.Value < commands.Count)
        {
            var failedIndex = result.FailedIndex.Value;
            var commandType = commands[failedIndex].GetType();
            MediationResult? refusal = null;

            foreach (var entry in result.Entries)
            {
                if (entry.Index == failedIndex)
                {
                    refusal = entry.Result;
                    break;
                }
            }

            if (refusal.HasValue && refusal.Value.IsDenied)
            {
                return new LiteBusMessageDeniedException(
                    commandType,
                    refusal.Value.Reason ?? "The test batch mediator refused the command.",
                    refusal.Value.Code);
            }

            if (refusal.HasValue && refusal.Value.IsInvalid)
            {
                return new LiteBusMessageInvalidException(commandType, refusal.Value.Failures);
            }
        }

        return new InvalidOperationException("The test batch reported RolledBack without naming a refused command.");
    }
}
