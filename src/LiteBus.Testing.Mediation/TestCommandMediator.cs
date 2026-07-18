using LiteBus.Commands.Abstractions;

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
    ///     Gets the commands recorded since construction or the last <see cref="Clear" /> call.
    /// </summary>
    public IReadOnlyList<object> Commands => _commands;

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

    /// <summary>
    ///     Clears recorded commands.
    /// </summary>
    public void Clear()
    {
        _commands.Clear();
    }
}