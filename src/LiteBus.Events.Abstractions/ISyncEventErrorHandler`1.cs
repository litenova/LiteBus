using LiteBus.Messaging.Abstractions;

namespace LiteBus.Events.Abstractions;

/// <summary>
///     Represents an action that is executed on <typeparamref cref="TEvent" /> error-handle phase
/// </summary>
public interface ISyncEventErrorHandler<in TEvent> : IEventErrorHandlerBase, ISyncErrorHandler<TEvent>
    where TEvent : IEvent
{
}