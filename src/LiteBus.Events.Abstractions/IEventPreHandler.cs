using LiteBus.Messaging.Abstractions;

namespace LiteBus.Events.Abstractions;

/// <summary>
///     Represents an action that is executed on each event pre-handle phase
/// </summary>
public interface IEventPreHandler : IEventHandler, IAsyncPreHandler<IEvent>
{
}