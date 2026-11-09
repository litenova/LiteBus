using System;
using System.Collections.Generic;
using System.Linq;
using LiteBus.Messaging.Abstractions;
using LiteBus.Runtime.Abstractions.Extensions;
using MessagingEnumerableExtensions = LiteBus.Messaging.Extensions.EnumerableExtensions;

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
    ///     The fault refusal classifiers, resolved from the dispatch scope on first use so that offering a fault never
    ///     creates the scope for a mediation that never faults.
    /// </summary>
    private readonly Lazy<IReadOnlyList<IFaultRefusalClassifier>> _faultRefusalClassifiers;

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
        _faultRefusalClassifiers = new Lazy<IReadOnlyList<IFaultRefusalClassifier>>(
            () => ResolveFaultRefusalClassifiers(serviceProvider));
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
    public IReadOnlyList<IFaultRefusalClassifier> FaultRefusalClassifiers => _faultRefusalClassifiers.Value;

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
    ///     Resolves the registered fault refusal classifiers from the dispatch scope.
    /// </summary>
    /// <param name="serviceProvider">The service provider handlers resolve from.</param>
    /// <returns>The registered classifiers in registration order, or an empty list when there are none.</returns>
    /// <remarks>
    ///     The collection resolves first, because that is how the messaging module registers classifiers. A single
    ///     classifier registered directly against the service type is honored as a one-item collection, so a host
    ///     that wires one by hand without the module builder still classifies.
    /// </remarks>
    private static IFaultRefusalClassifier[] ResolveFaultRefusalClassifiers(IServiceProvider serviceProvider)
    {
        if (serviceProvider.GetService(typeof(IEnumerable<IFaultRefusalClassifier>)) is IEnumerable<IFaultRefusalClassifier> classifiers)
        {
            return classifiers.ToArray();
        }

        if (serviceProvider.GetService(typeof(IFaultRefusalClassifier)) is IFaultRefusalClassifier classifier)
        {
            return [classifier];
        }

        return [];
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
        return MessagingEnumerableExtensions.ToLazyReadOnlyCollection(descriptors
            .Where(d => _handlerPredicate(d))
            .Where(d => d.Tags.Count == 0 || d.Tags.Intersect(_tags).Any())
            .Where(d => AppliesToMessage(d))
            .OrderBy(d => d.Priority)
            .ThenBy(d => d.RegistrationSequence)
            .Select(d => new LazyHandler<THandler, TDescriptor>
            {
                Handler = new Lazy<THandler>(() => resolveFunc(GetHandlerType(d))),
                Descriptor = d
            }));
    }

    /// <summary>
    ///     Determines whether a handler takes part in mediating the current runtime message type.
    /// </summary>
    /// <param name="descriptor">The handler descriptor linked to the message.</param>
    /// <returns>
    ///     <see langword="false" /> for an open generic handler that cannot be closed over the runtime message, because
    ///     its message type has another shape or its constraints reject the bound types; otherwise
    ///     <see langword="true" />.
    /// </returns>
    private bool AppliesToMessage(IHandlerDescriptor descriptor)
    {
        return !RuntimeHandlerBinder.IsOpen(descriptor) ||
               RuntimeHandlerBinder.TryClose(descriptor, _messageType) is not null;
    }

    /// <summary>
    ///     Retrieves the handler type to resolve for the current runtime message type.
    /// </summary>
    /// <param name="descriptor">The handler descriptor whose service type should be resolved.</param>
    /// <returns>
    ///     The handler type closed over the runtime message for an open generic handler, or the registered handler type
    ///     for every other handler, including a closed handler of a closed generic message.
    /// </returns>
    private Type GetHandlerType(IHandlerDescriptor descriptor)
    {
        return RuntimeHandlerBinder.IsOpen(descriptor)
            ? RuntimeHandlerBinder.Close(descriptor, _messageType).HandlerType
            : descriptor.HandlerType;
    }
}
