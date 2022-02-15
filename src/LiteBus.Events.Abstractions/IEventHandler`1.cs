﻿using LiteBus.Messaging.Abstractions;

namespace LiteBus.Events.Abstractions;

/// <summary>
///     Represents an asynchronous event handler
/// </summary>
/// <typeparam name="TEvent">The type of event</typeparam>
public interface IEventHandler<in TEvent> : IEventHandler, IAsyncHandler<TEvent> where TEvent : IEvent
{
}