namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Defines a contract for an object that holds the dependencies needed to handle messages within a given context,
///     offering various collections of handlers to be used at different stages of message processing.
/// </summary>
public interface IMessageDependencies
{
    /// <summary>
    ///     Gets a lazy initialized read-only collection of direct message handlers. These handlers are primarily responsible
    ///     for handling messages they are registered to handle.
    /// </summary>
    /// <value>
    ///     The collection of direct message handlers.
    /// </value>
    ILazyHandlerCollection<IMessageHandler, IMainHandlerDescriptor> MainHandlers { get; }

    /// <summary>
    ///     Gets a lazy initialized read-only collection of indirect message handlers. These handlers can be used to handle
    ///     messages in a more general manner, potentially handling a variety of different message types or performing logging
    ///     or other cross-cutting concerns.
    /// </summary>
    /// <value>
    ///     The collection of indirect message handlers.
    /// </value>
    ILazyHandlerCollection<IMessageHandler, IMainHandlerDescriptor> IndirectMainHandlers { get; }

    /// <summary>
    ///     Gets a lazy initialized read-only collection of the pre-stage handlers registered directly for this message
    ///     type. Every pre-stage role shares this collection: guards, validators, shortcuts, and pre-handlers.
    /// </summary>
    /// <value>
    ///     The collection of direct pre-stage handlers, ordered once by priority. Read
    ///     <see cref="IPreStageHandlerDescriptor.Stage" /> to tell which role a handler plays.
    /// </value>
    ILazyHandlerCollection<IMessagePreStageHandler, IPreStageHandlerDescriptor> PreStageHandlers { get; }

    /// <summary>
    ///     Gets a lazy initialized read-only collection of the pre-stage handlers registered for a base type or interface
    ///     this message type implements, such as a guard covering every command.
    /// </summary>
    /// <value>
    ///     The collection of indirect pre-stage handlers. Within each stage these run before the direct ones, so a
    ///     cross-cutting concern wraps a message-specific one.
    /// </value>
    ILazyHandlerCollection<IMessagePreStageHandler, IPreStageHandlerDescriptor> IndirectPreStageHandlers { get; }

    /// <summary>
    ///     Gets a lazy initialized read-only collection of direct post-message handlers. These handlers are invoked after the
    ///     primary message handlers have completed their work, allowing for tasks such as cleanup or logging to be performed.
    /// </summary>
    /// <value>
    ///     The collection of direct post-message handlers.
    /// </value>
    ILazyHandlerCollection<IMessagePostHandler, IPostHandlerDescriptor> PostHandlers { get; }

    /// <summary>
    ///     Gets a lazy initialized read-only collection of indirect post-message handlers. These handlers are invoked after
    ///     the primary message handlers, potentially handling a variety of different message types or performing logging or
    ///     other cross-cutting concerns.
    /// </summary>
    /// <value>
    ///     The collection of indirect post-message handlers.
    /// </value>
    ILazyHandlerCollection<IMessagePostHandler, IPostHandlerDescriptor> IndirectPostHandlers { get; }

    /// <summary>
    ///     Gets a lazy initialized read-only collection of direct message error handlers. These handlers are invoked when an
    ///     error occurs during message processing, allowing for centralized error handling logic to be implemented.
    /// </summary>
    /// <value>
    ///     The collection of direct message error handlers.
    /// </value>
    ILazyHandlerCollection<IMessageErrorHandler, IErrorHandlerDescriptor> ErrorHandlers { get; }

    /// <summary>
    ///     Gets a lazy initialized read-only collection of indirect message error handlers. These handlers are invoked when an
    ///     error occurs during message processing, potentially handling a variety of different message types or performing
    ///     logging or other cross-cutting concerns.
    /// </summary>
    /// <value>
    ///     The collection of indirect message error handlers.
    /// </value>
    ILazyHandlerCollection<IMessageErrorHandler, IErrorHandlerDescriptor> IndirectErrorHandlers { get; }

    /// <summary>
    ///     Determines whether any handler is registered for the given stage of the pre stage.
    /// </summary>
    /// <param name="stage">The stage to test.</param>
    /// <returns><see langword="true" /> when at least one handler runs in that stage.</returns>
    /// <remarks>
    ///     Every pre-stage role shares one descriptor collection, so running a stage means a filtered pass over it. Most
    ///     messages have no guard, validator, or shortcut at all, and this lets those stages be skipped without
    ///     enumerating. The default implementation is correct for any implementation of this interface; LiteBus's own
    ///     answers from a mask computed once when the dependencies are resolved.
    /// </remarks>
    bool HasPreStageHandlers(PreStage stage)
    {
        foreach (var handler in PreStageHandlers)
        {
            if (handler.Descriptor.Stage == stage)
            {
                return true;
            }
        }

        foreach (var handler in IndirectPreStageHandlers)
        {
            if (handler.Descriptor.Stage == stage)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Gets the refusal mappers registered for this specific message type.
    /// </summary>
    ILazyHandlerCollection<IMessageRefusalMapper, IRefusalMapperDescriptor> RefusalMappers { get; }

    /// <summary>
    ///     Gets the refusal mappers registered for a base type or interface that this message type implements.
    /// </summary>
    ILazyHandlerCollection<IMessageRefusalMapper, IRefusalMapperDescriptor> IndirectRefusalMappers { get; }

    /// <summary>
    ///     Gets a lazy initialized read-only collection of direct completion handlers. These handlers are invoked once the
    ///     mediation ends, on every path, allowing the outcome of the operation to be observed and recorded.
    /// </summary>
    /// <value>
    ///     The collection of direct completion handlers.
    /// </value>
    ILazyHandlerCollection<IMessageCompletionHandler, ICompletionHandlerDescriptor> CompletionHandlers { get; }

    /// <summary>
    ///     Gets a lazy initialized read-only collection of indirect completion handlers. These handlers are invoked once the
    ///     mediation ends, potentially observing a variety of different message types.
    /// </summary>
    /// <value>
    ///     The collection of indirect completion handlers.
    /// </value>
    ILazyHandlerCollection<IMessageCompletionHandler, ICompletionHandlerDescriptor> IndirectCompletionHandlers { get; }
}
