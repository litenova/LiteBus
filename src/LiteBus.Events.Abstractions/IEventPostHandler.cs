using LiteBus.Messaging.Abstractions;

namespace LiteBus.Events.Abstractions;

/// <summary>
///     Represents an action that is executed on each event post-handle phase
/// </summary>
public interface IEventPostHandler : IAsyncMessagePostHandler<IEvent>, IRegistrableEventConstruct
{
}