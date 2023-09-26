namespace LiteBus.Messaging.Abstractions;

/// <summary>
/// Defines a contract for an object that holds the dependencies needed to handle messages within a given context, offering various collections of handlers to be used at different stages of message processing.
/// </summary>
public interface IMessageDependencies
{
    /// <summary>
    /// Gets a lazy initialized read-only collection of direct message handlers. These handlers are primarily responsible for handling messages they are registered to handle.
    /// </summary>
    /// <value>
    /// The collection of direct message handlers.
    /// </value>
    ILazyReadOnlyCollection<IMessageHandler> Handlers { get; }

    /// <summary>
    /// Gets a lazy initialized read-only collection of indirect message handlers. These handlers can be used to handle messages in a more general manner, potentially handling a variety of different message types or performing logging or other cross-cutting concerns.
    /// </summary>
    /// <value>
    /// The collection of indirect message handlers.
    /// </value>
    ILazyReadOnlyCollection<IMessageHandler> IndirectHandlers { get; }

    /// <summary>
    /// Gets a lazy initialized read-only collection of direct pre-message handlers. These handlers are invoked before the primary message handlers and can be used for tasks such as validation or logging.
    /// </summary>
    /// <value>
    /// The collection of direct pre-message handlers.
    /// </value>
    ILazyReadOnlyCollection<IMessagePreHandler> PreHandlers { get; }

    /// <summary>
    /// Gets a lazy initialized read-only collection of indirect pre-message handlers. These handlers are invoked before the primary message handlers, potentially handling a variety of different message types or performing logging or other cross-cutting concerns.
    /// </summary>
    /// <value>
    /// The collection of indirect pre-message handlers.
    /// </value>
    ILazyReadOnlyCollection<IMessagePreHandler> IndirectPreHandlers { get; }

    /// <summary>
    /// Gets a lazy initialized read-only collection of direct post-message handlers. These handlers are invoked after the primary message handlers have completed their work, allowing for tasks such as cleanup or logging to be performed.
    /// </summary>
    /// <value>
    /// The collection of direct post-message handlers.
    /// </value>
    ILazyReadOnlyCollection<IMessagePostHandler> PostHandlers { get; }

    /// <summary>
    /// Gets a lazy initialized read-only collection of indirect post-message handlers. These handlers are invoked after the primary message handlers, potentially handling a variety of different message types or performing logging or other cross-cutting concerns.
    /// </summary>
    /// <value>
    /// The collection of indirect post-message handlers.
    /// </value>
    ILazyReadOnlyCollection<IMessagePostHandler> IndirectPostHandlers { get; }

    /// <summary>
    /// Gets a lazy initialized read-only collection of direct message error handlers. These handlers are invoked when an error occurs during message processing, allowing for centralized error handling logic to be implemented.
    /// </summary>
    /// <value>
    /// The collection of direct message error handlers.
    /// </value>
    ILazyReadOnlyCollection<IMessageErrorHandler> ErrorHandlers { get; }

    /// <summary>
    /// Gets a lazy initialized read-only collection of indirect message error handlers. These handlers are invoked when an error occurs during message processing, potentially handling a variety of different message types or performing logging or other cross-cutting concerns.
    /// </summary>
    /// <value>
    /// The collection of indirect message error handlers.
    /// </value>
    ILazyReadOnlyCollection<IMessageErrorHandler> IndirectErrorHandlers { get; }
}