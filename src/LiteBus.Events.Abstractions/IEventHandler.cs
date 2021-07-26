﻿using LiteBus.Messaging.Abstractions;

namespace LiteBus.Events.Abstractions
{
    /// <summary>
    ///     Represents an asynchronous event handler
    /// </summary>
    /// <typeparam name="TEvent">The type of event</typeparam>
    public interface IEventHandler<TEvent> : IAsyncMessageHandler<TEvent> where TEvent : IEvent
    {
        
    }
}