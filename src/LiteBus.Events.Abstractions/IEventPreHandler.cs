using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Events.Abstractions
{
    public interface IEventPreHandlerBase
    {
    }

    /// <summary>
    ///     Represents an action that is executed on each event pre-handle phase
    /// </summary>
    public interface IEventPreHandler : IEventPreHandlerBase, IMessagePreHandler<IEvent>
    {
    }

    /// <summary>
    ///     Represents an action that is executed on <typeparamref cref="TEvent" /> pre-handle phase
    /// </summary>
    public interface IEventPreHandler<in TEvent> : IEventPreHandlerBase, IMessagePreHandler<TEvent>
        where TEvent : IEvent
    {
    }
}