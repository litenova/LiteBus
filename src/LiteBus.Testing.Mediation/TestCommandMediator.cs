using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Testing;

/// <summary>
///     Records commands sent through <see cref="ICommandMediator" /> for test assertions.
/// </summary>
/// <remarks>
///     All members are thread-safe. Recording properties return snapshots, so a held reference never observes later
///     sends and mutating it never affects the recorded history.
/// </remarks>
public sealed class TestCommandMediator : ICommandMediator
{
    /// <summary>
    ///     Gets commands recorded by <see cref="SendAsync" /> overloads.
    /// </summary>
    private readonly List<object> _commands = [];

    /// <summary>
    ///     Commands recorded by <see cref="EvaluateAsync" />, kept apart because evaluating is not sending.
    /// </summary>
    private readonly List<object> _evaluated = [];

    /// <summary>
    ///     Gets the lock that serializes recording mutations and snapshot reads.
    /// </summary>
    private readonly object _sync = new();

    /// <summary>
    ///     Gets a snapshot of the commands recorded since construction or the last <see cref="Clear" /> call.
    /// </summary>
    public IReadOnlyList<object> Commands
    {
        get
        {
            lock (_sync)
            {
                return [.. _commands];
            }
        }
    }

    /// <summary>
    ///     Gets a snapshot of the commands evaluated since construction or the last <see cref="Clear" /> call.
    /// </summary>
    /// <value>
    ///     Separate from <see cref="Commands" />, because an evaluation asks whether a command may happen and does not
    ///     perform it. Asserting that a control evaluated a command is a different assertion from asserting it sent
    ///     one.
    /// </value>
    public IReadOnlyList<object> Evaluated
    {
        get
        {
            lock (_sync)
            {
                return [.. _evaluated];
            }
        }
    }

    /// <inheritdoc />
    public Task SendAsync(
        ICommand command,
        CommandMediationSettings? commandMediationSettings = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        lock (_sync)
        {
            _commands.Add(command);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<TCommandResult> SendAsync<TCommandResult>(
        ICommand<TCommandResult> command,
        CommandMediationSettings? commandMediationSettings = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        lock (_sync)
        {
            _commands.Add(command);
        }

        return Task.FromResult(default(TCommandResult)!);
    }

    /// <inheritdoc />
    public Task<MediationResult> TrySendAsync(
        ICommand command,
        CommandMediationSettings? commandMediationSettings = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        lock (_sync)
        {
            _commands.Add(command);
        }

        return Task.FromResult(MediationResult.Succeeded());
    }

    /// <inheritdoc />
    public Task<MediationResult<TCommandResult>> TrySendAsync<TCommandResult>(
        ICommand<TCommandResult> command,
        CommandMediationSettings? commandMediationSettings = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        lock (_sync)
        {
            _commands.Add(command);
        }

        return Task.FromResult(MediationResult<TCommandResult>.Succeeded(default!));
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Records the command and permits it. A recording double runs no guards, so it can only answer that nothing
    ///     objected; assert on a real pipeline when the decision itself is what is under test.
    /// </remarks>
    public Task<MediationDecision> EvaluateAsync(
        ICommand command,
        CommandMediationSettings? commandMediationSettings = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        lock (_sync)
        {
            _evaluated.Add(command);
        }

        return Task.FromResult(MediationDecision.Allowed);
    }

    /// <summary>
    ///     Clears recorded commands.
    /// </summary>
    public void Clear()
    {
        lock (_sync)
        {
            _commands.Clear();
            _evaluated.Clear();
        }
    }
}
