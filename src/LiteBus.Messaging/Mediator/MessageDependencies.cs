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
    ///     One bit per <see cref="PreStage" /> that has at least one handler, computed once at resolution.
    /// </summary>
    private readonly int _occupiedPreStages;

    /// <summary>
    ///     The mediation tags used to filter handlers by tag intersection.
    /// </summary>
    private readonly IReadOnlyCollection<string> _tags;

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
        _tags = tags as IReadOnlyCollection<string> ?? tags.ToList();
        _handlerPredicate = handlerPredicate;

        MainHandlers = ResolveHandlers(descriptor.Handlers, handlerType => (IMessageHandler) serviceProvider.GetRequiredService(handlerType));
        IndirectMainHandlers = ResolveHandlers(descriptor.IndirectHandlers, handlerType => (IMessageHandler) serviceProvider.GetRequiredService(handlerType));

        PreStageHandlers = ResolveHandlers(descriptor.PreStageHandlers, handlerType => (IMessagePreStageHandler) serviceProvider.GetRequiredService(handlerType));
        IndirectPreStageHandlers = ResolveHandlers(descriptor.IndirectPreStageHandlers, handlerType => (IMessagePreStageHandler) serviceProvider.GetRequiredService(handlerType));

        PostHandlers = ResolveHandlers(descriptor.PostHandlers, handlerType => (IMessagePostHandler) serviceProvider.GetRequiredService(handlerType));
        IndirectPostHandlers = ResolveHandlers(descriptor.IndirectPostHandlers, handlerType => (IMessagePostHandler) serviceProvider.GetRequiredService(handlerType));

        ErrorHandlers = ResolveHandlers(descriptor.ErrorHandlers, handlerType => (IMessageErrorHandler) serviceProvider.GetRequiredService(handlerType));
        IndirectErrorHandlers = ResolveHandlers(descriptor.IndirectErrorHandlers, handlerType => (IMessageErrorHandler) serviceProvider.GetRequiredService(handlerType));

        // The completion stage orders by priority alone, so the two descriptor sets are resolved as one collection
        // rather than kept apart the way every wrapping role keeps them apart.
        CompletionHandlers = ResolveHandlers(
            descriptor.CompletionHandlers.Concat(descriptor.IndirectCompletionHandlers),
            handlerType => (IMessageCompletionHandler) serviceProvider.GetRequiredService(handlerType));
        RefusalMappers = ResolveHandlers(descriptor.RefusalMappers, handlerType => (IMessageRefusalMapper) serviceProvider.GetRequiredService(handlerType));
        IndirectRefusalMappers = ResolveHandlers(descriptor.IndirectRefusalMappers, handlerType => (IMessageRefusalMapper) serviceProvider.GetRequiredService(handlerType));
        _occupiedPreStages = ComputeOccupiedPreStages(PreStageHandlers, IndirectPreStageHandlers);
    }

    /// <inheritdoc />
    public ILazyHandlerCollection<IMessageHandler, IMainHandlerDescriptor> MainHandlers { get; }

    /// <inheritdoc />
    public ILazyHandlerCollection<IMessageHandler, IMainHandlerDescriptor> IndirectMainHandlers { get; }

    /// <inheritdoc />
    public ILazyHandlerCollection<IMessagePreStageHandler, IPreStageHandlerDescriptor> PreStageHandlers { get; }

    /// <inheritdoc />
    public ILazyHandlerCollection<IMessagePreStageHandler, IPreStageHandlerDescriptor> IndirectPreStageHandlers { get; }

    /// <inheritdoc />
    public ILazyHandlerCollection<IMessagePostHandler, IPostHandlerDescriptor> PostHandlers { get; }

    /// <inheritdoc />
    public ILazyHandlerCollection<IMessagePostHandler, IPostHandlerDescriptor> IndirectPostHandlers { get; }

    /// <inheritdoc />
    public ILazyHandlerCollection<IMessageErrorHandler, IErrorHandlerDescriptor> ErrorHandlers { get; }

    /// <inheritdoc />
    public ILazyHandlerCollection<IMessageErrorHandler, IErrorHandlerDescriptor> IndirectErrorHandlers { get; }

    /// <inheritdoc />
    public ILazyHandlerCollection<IMessageCompletionHandler, ICompletionHandlerDescriptor> CompletionHandlers { get; }

    /// <inheritdoc />
    public ILazyHandlerCollection<IMessageRefusalMapper, IRefusalMapperDescriptor> RefusalMappers { get; }

    /// <inheritdoc />
    public ILazyHandlerCollection<IMessageRefusalMapper, IRefusalMapperDescriptor> IndirectRefusalMappers { get; }

    /// <inheritdoc />
    public bool HasPreStageHandlers(PreStage stage)
    {
        return (_occupiedPreStages & (1 << (int) stage)) != 0;
    }

    /// <summary>
    ///     Builds the stage-occupancy mask from the resolved pre-stage descriptors.
    /// </summary>
    /// <param name="preHandlers">The pre-stage handlers registered for the message type itself.</param>
    /// <param name="indirectPreHandlers">The pre-stage handlers registered for a base type or interface.</param>
    /// <returns>A mask with one bit set per stage that has at least one handler.</returns>
    private static int ComputeOccupiedPreStages(
        ILazyHandlerCollection<IMessagePreStageHandler, IPreStageHandlerDescriptor> preHandlers,
        ILazyHandlerCollection<IMessagePreStageHandler, IPreStageHandlerDescriptor> indirectPreHandlers)
    {
        var mask = 0;

        foreach (var preHandler in preHandlers)
        {
            mask |= 1 << (int) preHandler.Descriptor.Stage;
        }

        foreach (var preHandler in indirectPreHandlers)
        {
            mask |= 1 << (int) preHandler.Descriptor.Stage;
        }

        return mask;
    }

    /// <summary>
    ///     Resolves handlers from the provided descriptors and a handler resolution function.
    /// </summary>
    /// <typeparam name="THandler">The handler contract type to resolve.</typeparam>
    /// <typeparam name="TDescriptor">The descriptor type that supplies handler metadata.</typeparam>
    /// <param name="descriptors">The handler descriptors to filter, order, and resolve.</param>
    /// <param name="resolveFunc">The function that resolves a handler instance from its service type.</param>
    /// <returns>A lazy read-only collection of resolved handlers and their descriptors.</returns>
    /// <remarks>
    ///     Mediation tags use independent (OR) matching: a tagged handler participates when it shares at least one tag
    ///     with the active mediation tags. Untagged handlers always participate. When more than one main handler remains
    ///     after filtering, <see cref="MultipleHandlerFoundException" /> is thrown during handler resolution.
    /// </remarks>
    private ILazyHandlerCollection<THandler, TDescriptor> ResolveHandlers<THandler, TDescriptor>(
        IEnumerable<TDescriptor> descriptors,
        Func<Type, THandler> resolveFunc) where TDescriptor : IHandlerDescriptor
    {
        return descriptors
            .Where(d => _handlerPredicate(d))
            .Where(d => d.Tags.Count == 0 || d.Tags.Intersect(_tags).Any())
            .OrderBy(d => d.Priority)
            .ThenBy(d => d.RegistrationSequence)
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