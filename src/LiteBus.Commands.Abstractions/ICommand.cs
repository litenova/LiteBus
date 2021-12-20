namespace LiteBus.Commands.Abstractions
{
    public interface ICommandBase : ICommandConstruct
    {
    }

    /// <summary>
    ///     Represents a command without result
    /// </summary>
    public interface ICommand : ICommandBase
    {
    }

    /// <summary>
    ///     Represents a command with result
    /// </summary>
    public interface ICommand<TCommandResult> : ICommandBase
    {
    }
}