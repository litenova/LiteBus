using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Testing;

/// <summary>
///     Records commands sent through <see cref="ICommandMediator" /> for test assertions.
/// </summary>
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
    ///     Gets the commands recorded since construction or the last <see cref="Clear" /> call.
    /// </summary>
    public IReadOnlyList<object> Commands => _commands;

    /// <summary>
    ///     Gets the commands evaluated since construction or the last <see cref="Clear" /> call.
    /// </summary>
    /// <value>
    ///     Separate from <see cref="Commands" />, because an evaluation asks whether a command may happen and does not
    ///     perform it. Asserting that a control evaluated a command is a different assertion from asserting it sent
    ///     one.
    /// </value>
    public IReadOnlyList<object> Evaluated => _evaluated;

    /// <inheritdoc />
    public Task SendAsync(
        ICommand command,
        CommandMediationSettings? commandMediationSettings = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        _commands.Add(command);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<TCommandResult> SendAsync<TCommandResult>(
        ICommand<TCommandResult> command,
        CommandMediationSettings? commandMediationSettings = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        _commands.Add(command);
        return Task.FromResult(default(TCommandResult)!);
    }

    /// <inheritdoc />
    public Task<MediationResult> TrySendAsync(
        ICommand command,
        CommandMediationSettings? commandMediationSettings = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        _commands.Add(command);
        return Task.FromResult(MediationResult.Succeeded());
    }

    /// <inheritdoc />
    public Task<MediationResult<TCommandResult>> TrySendAsync<TCommandResult>(
        ICommand<TCommandResult> command,
        CommandMediationSettings? commandMediationSettings = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        _commands.Add(command);
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
        _evaluated.Add(command);
        return Task.FromResult(MediationDecision.Allowed);
    }

    /// <summary>
    ///     Clears recorded commands.
    /// </summary>
    public void Clear()
    {
        _commands.Clear();
        _evaluated.Clear();
    }
}