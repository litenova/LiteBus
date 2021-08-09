namespace LiteBus.Commands.Abstractions
{
    /// <summary>
    ///     Represents a command without result
    /// </summary>
    public interface ICommand
    {
    }

    /// <summary>
    ///     Represents a command with result
    /// </summary>
    public interface ICommand<TCommandResult> : ICommand
    {
        
    }
}