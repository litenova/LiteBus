namespace BasicBus.Abstractions
{
    /// <summary>
    /// Represents a command that is intended to change the application state
    /// </summary>
    public class ICommand
    {
        
    }
    
    /// <summary>
    /// Represents a command with result that is intended to change the application state
    /// </summary>
    public class ICommand<TCommandResult> : ICommand
    {
        
    }
}