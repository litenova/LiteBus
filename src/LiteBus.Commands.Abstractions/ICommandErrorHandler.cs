using LiteBus.Messaging.Abstractions;

namespace LiteBus.Commands.Abstractions;

/// <summary>
///     Represents an action that is executed on each command error-handle phase
/// </summary>
public interface ICommandErrorHandler : IAsyncErrorHandler<ICommand>
{
}