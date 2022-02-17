namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Represents the execution workflow of a given message
/// </summary>
/// <typeparam name="TMessage">Type of message</typeparam>
/// <typeparam name="TMessageResult">Type of message result</typeparam>
public interface IExecutionWorkflow<in TMessage, out TMessageResult> where TMessage : notnull
{
    TMessageResult Execute(TMessage message, IResolutionContext resolutionContext);
}