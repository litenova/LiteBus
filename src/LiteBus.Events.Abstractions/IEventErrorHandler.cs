using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Events.Abstractions
{
    public interface IEventErrorHandlerBase : IEventConstruct
    {
    }

    /// <summary>
    ///     Represents an action that is executed on each event error-handle phase
    /// </summary>
    public interface IEventErrorHandler : IEventErrorHandlerBase, IMessageErrorHandler<IEvent>
    {
    }

    /// <summary>
    ///     Represents an action that is executed on <typeparamref cref="TEvent" /> error-handle phase
    /// </summary>
    public interface IEventErrorHandler<in TEvent> : IEventErrorHandlerBase, IMessageErrorHandler<TEvent>
        where TEvent : IEvent
    {
    }
}