namespace LiteBus.Commands.Abstractions;

/// <summary>
///     Represents a command with explicit result
/// </summary>
// ReSharper disable once UnusedTypeParameter
public interface ICommand<TCommandResult> : ICommand
{
}