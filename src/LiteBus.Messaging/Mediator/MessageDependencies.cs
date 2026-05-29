using System;
using System.Collections.Generic;
using System.Linq;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Extensions;

namespace LiteBus.Messaging.Mediator;

/// <inheritdoc cref="IMessageDependencies" />
internal sealed class MessageDependencies : IMessageDependencies
{
    /// <summary>
    ///     Filters handler descriptors before they are resolved from the service provider.
    /// </summary>
    private readonly Func<IHandlerDescriptor, bool> _handlerPredicate;

    /// <summary>
    ///     The concrete runtime type of the message being mediated.
    /// </summary>
    private readonly Type _messageType;

    /// <summary>
    ///     The mediation tags used to filter handlers by tag intersection.
    /// </summary>
    private readonly IEnumerable<string> _tags;

    /// <summary>
    ///     Initializes a new instance of the <see cref="MessageDependencies" /> class.
    /// </summary>
    /// <param name="messageType">The concrete runtime type of the message being mediated.</param>
    /// <param name="descriptor">The message descriptor that supplies handler collections.</param>
    /// <param name="serviceProvider">The service provider used to resolve handler instances.</param>
    /// <param name="tags">The mediation tags used to filter handlers.</param>
    /// <param name="handlerPredicate">The predicate that filters handler descriptors before resolution.</param>
    public MessageDependencies(Type messageType,
                               IMessageDescriptor descriptor,
                               IServiceProvider serviceProvider,
                               IEnumerable<string> tags,
                               Func<IHandlerDescriptor, bool> handlerPredicate)
    {
        _messageType = messageType;
        _tags = tags;
        _handlerPredicate = handlerPredicate;

        MainHandlers = ResolveHandlers(descriptor.Handlers, handlerType => (IMessageHandler) serviceProvider.GetRequiredService(handlerType));
        IndirectMainHandlers = ResolveHandlers(descriptor.IndirectHandlers, handlerType => (IMessageHandler) serviceProvider.GetRequiredService(handlerType));

        PreHandlers = ResolveHandlers(descriptor.PreHandlers, handlerType => (IMessagePreHandler) serviceProvider.GetRequiredService(handlerType));
        IndirectPreHandlers = ResolveHandlers(descriptor.IndirectPreHandlers, handlerType => (IMessagePreHandler) serviceProvider.GetRequiredService(handlerType));

        PostHandlers = ResolveHandlers(descriptor.PostHandlers, handlerType => (IMessagePostHandler) serviceProvider.GetRequiredService(handlerType));
        IndirectPostHandlers = ResolveHandlers(descriptor.IndirectPostHandlers, handlerType => (IMessagePostHandler) serviceProvider.GetRequiredService(handlerType));

        ErrorHandlers = ResolveHandlers(descriptor.ErrorHandlers, handlerType => (IMessageErrorHandler) serviceProvider.GetRequiredService(handlerType));
        IndirectErrorHandlers = ResolveHandlers(descriptor.IndirectErrorHandlers, handlerType => (IMessageErrorHandler) serviceProvider.GetRequiredService(handlerType));
    }

    /// <inheritdoc />
    public ILazyHandlerCollection<IMessageHandler, IMainHandlerDescriptor> MainHandlers { get; }

    /// <inheritdoc />
    public ILazyHandlerCollection<IMessageHandler, IMainHandlerDescriptor> IndirectMainHandlers { get; }

    /// <inheritdoc />
    public ILazyHandlerCollection<IMessagePreHandler, IPreHandlerDescriptor> PreHandlers { get; }

    /// <inheritdoc />
    public ILazyHandlerCollection<IMessagePreHandler, IPreHandlerDescriptor> IndirectPreHandlers { get; }

    /// <inheritdoc />
    public ILazyHandlerCollection<IMessagePostHandler, IPostHandlerDescriptor> PostHandlers { get; }

    /// <inheritdoc />
    public ILazyHandlerCollection<IMessagePostHandler, IPostHandlerDescriptor> IndirectPostHandlers { get; }

    /// <inheritdoc />
    public ILazyHandlerCollection<IMessageErrorHandler, IErrorHandlerDescriptor> ErrorHandlers { get; }

    /// <inheritdoc />
    public ILazyHandlerCollection<IMessageErrorHandler, IErrorHandlerDescriptor> IndirectErrorHandlers { get; }

    /// <summary>
    ///     Resolves handlers from the provided descriptors and a handler resolution function.
    /// </summary>
    /// <typeparam name="THandler">The handler contract type to resolve.</typeparam>
    /// <typeparam name="TDescriptor">The descriptor type that supplies handler metadata.</typeparam>
    /// <param name="descriptors">The handler descriptors to filter, order, and resolve.</param>
    /// <param name="resolveFunc">The function that resolves a handler instance from its service type.</param>
    /// <returns>A lazy read-only collection of resolved handlers and their descriptors.</returns>
    private ILazyHandlerCollection<THandler, TDescriptor> ResolveHandlers<THandler, TDescriptor>(
        IEnumerable<TDescriptor> descriptors,
        Func<Type, THandler> resolveFunc) where TDescriptor : IHandlerDescriptor
    {
        return descriptors
            .Where(d => _handlerPredicate(d))
            .Where(d => d.Tags.Count == 0 || d.Tags.Intersect(_tags).Any())
            .OrderBy(d => d.Priority)
            .Select(d => new LazyHandler<THandler, TDescriptor>
            {
                Handler = new Lazy<THandler>(() => resolveFunc(GetHandlerType(d))),
                Descriptor = d
            })
            .ToLazyReadOnlyCollection();
    }

    /// <summary>
    ///     Retrieves the handler type from a descriptor and closes only open generic handler definitions for the current
    ///     runtime message type. Closed concrete handlers for closed generic messages must be resolved as registered.
    /// </summary>
    /// <param name="descriptor">The handler descriptor whose service type should be resolved.</param>
    /// <returns>The closed handler type used for dependency injection resolution.</returns>
    private Type GetHandlerType(IHandlerDescriptor descriptor)
    {
        var handlerType = descriptor.HandlerType;

        if (descriptor.MessageType.IsGenericType && handlerType.IsGenericTypeDefinition)
        {
            handlerType = handlerType.MakeGenericType(_messageType.GetGenericArguments());
        }

        return handlerType;
    }
}
