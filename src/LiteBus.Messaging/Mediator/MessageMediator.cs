using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using LiteBus.Messaging.Abstractions;
using LiteBus.Runtime.Abstractions;
using ExecutionContext = LiteBus.Messaging.Contexts.Execution.ExecutionContext;

namespace LiteBus.Messaging.Mediator;

/// <summary>
///     Implements the <see cref="IMessageMediator" /> interface to provide message mediation capabilities.
/// </summary>
/// <remarks>
///     The <see cref="MessageMediator" /> is responsible for handling the mediation of messages by:
///     <list type="bullet">
///         <item>
///             <description>Creating and managing execution contexts for each mediation operation</description>
///         </item>
///         <item>
///             <description>Resolving message handlers through the message reader</description>
///         </item>
///         <item>
///             <description>Applying the appropriate mediation strategy to process messages</description>
///         </item>
///         <item>
///             <description>Managing nested mediation calls by preserving execution context state</description>
///         </item>
///         <item>
///             <description>Evaluating registered mediation nesting rules before a nested mediation starts</description>
///         </item>
///     </list>
/// </remarks>
internal sealed class MessageMediator : IMessageMediator
{
    /// <summary>
    ///     The message reader used to resolve message descriptors during mediation.
    /// </summary>
    private readonly IMessageReader _messageReader;

    /// <summary>
    ///     The message writer used when plain messages are registered on the spot.
    /// </summary>
    private readonly IMessageWriter _messageWriter;

    /// <summary>
    ///     The factory that creates per-mediation dependency injection scopes for handler resolution.
    /// </summary>
    private readonly IMessageDispatchScopeFactory _dispatchScopeFactory;

    /// <summary>
    ///     The clock each mediation's handled instant is read from.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    ///     Initializes a new instance of the <see cref="MessageMediator" /> class.
    /// </summary>
    /// <param name="messageReader">The reader containing message handler information.</param>
    /// <param name="messageWriter">The writer used for on-the-spot message registration.</param>
    /// <param name="dispatchScopeFactory">The factory that creates per-mediation dependency injection scopes.</param>
    /// <param name="timeProvider">
    ///     The clock each mediation's handled instant is read from. The container supplies the one the messaging
    ///     module registered; a harness that builds the mediator by hand gets <see cref="TimeProvider.System" />.
    /// </param>
    public MessageMediator(
        IMessageReader messageReader,
        IMessageWriter messageWriter,
        IMessageDispatchScopeFactory dispatchScopeFactory,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(messageReader);
        _messageReader = messageReader;
        ArgumentNullException.ThrowIfNull(messageWriter);
        _messageWriter = messageWriter;
        ArgumentNullException.ThrowIfNull(dispatchScopeFactory);
        _dispatchScopeFactory = dispatchScopeFactory;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    ///     Mediates a message to its appropriate handler and returns the result.
    /// </summary>
    /// <typeparam name="TMessage">The type of the message.</typeparam>
    /// <typeparam name="TMessageResult">The type of the result.</typeparam>
    /// <param name="message">The message to mediate.</param>
    /// <param name="request">The request that controls the mediation process.</param>
    /// <param name="cancellationToken">The token used to cancel the mediation process.</param>
    /// <returns>The result of the message handling.</returns>
    /// <remarks>
    ///     When <see cref="MessageMediationRequest{TMessage, TMessageResult}.DispatchScope" /> is set, handlers and
    ///     scoped services resolve from that scope and the mediator never disposes it, including when the result is
    ///     asynchronous; the owner disposes it. When it is <see langword="null" />, the mediator creates one
    ///     <see cref="LazyMessageDispatchScope" /> for the call and disposes it when the pipeline completes.
    ///     When the request carries a <see cref="MediationCause" />, the mediation starts as a root with that cause
    ///     and no parent, even inside an ambient scope, so boundary delivery never nests and never inherits the
    ///     enclosing identity.
    /// </remarks>
    /// <exception cref="NoHandlerFoundException">
    ///     Thrown when no handler is found for the message type and registration on spot
    ///     is disabled.
    /// </exception>
    /// <exception cref="MessageDescriptorNotFoundException">
    ///     Thrown when no descriptor can be found for the message type with the
    ///     specified resolve strategy.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="message" /> or <paramref name="request" /> is <see langword="null" />.
    /// </exception>
    /// <exception cref="NestedMediationException">
    ///     Thrown when a registered mediation nesting rule denies the nested mediation.
    /// </exception>
    public TMessageResult Mediate<TMessage, TMessageResult>(
        TMessage message,
        MessageMediationRequest<TMessage, TMessageResult> request,
        CancellationToken cancellationToken = default)
        where TMessage : notnull
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(request);

        // The dispatch scope is created before the execution context because identity resolution reads the registered
        // identity provider from it. A caller-supplied scope is resolved from but never disposed here.
        var externalScope = request.DispatchScope;
        LazyMessageDispatchScope? ownedScope;
        IMessageDispatchScope dispatchScope;

        if (externalScope is not null)
        {
            ownedScope = null;
            dispatchScope = externalScope;
        }
        else
        {
            ownedScope = new LazyMessageDispatchScope(_dispatchScopeFactory);
            dispatchScope = ownedScope;
        }

        // A cause marks a root mediation started by a boundary, so it never inherits the ambient scope even
        // when one exists. Otherwise the mediation nests inside the ambient scope when there is one.
        var cause = request.Cause;
        var ambientContext = AmbientExecutionContext.GetCurrentOrDefault();
        var parentContext = cause is null ? ambientContext : null;
        var identity = ResolveIdentity(
            request.Identity,
            parentContext,
            dispatchScope);

        // Retain only the dispatch scope until asynchronous mediation results complete. The ambient scope must not leak
        // into the caller after this method returns.
        AmbientExecutionContext.ExecutionContextScope? executionScope = null;

        try
        {
            // Get the actual type of the message.
            var messageType = message.GetType();

            ThrowIfNestingDenied(parentContext, messageType, dispatchScope);

            // Create a new execution context for the current scope.
            var executionContext = new ExecutionContext(
                request.Tags,
                request.Items,
                _timeProvider,
                identity,
                cancellationToken,
                messageType,
                parentContext,
                cause);

            executionScope = AmbientExecutionContext.CreateScope(executionContext);

            RegisterWhenCoveredOnlyThroughOtherTypes(messageType);

            // Find the message descriptor.
            var descriptor = request.MessageResolveStrategy.Find(messageType, _messageReader);

            if (descriptor is null)
            {
                if (!request.RegisterPlainMessagesOnSpot)
                {
                    throw new NoHandlerFoundException(messageType);
                }

                _messageWriter.Register(messageType);

                descriptor = request.MessageResolveStrategy.Find(messageType, _messageReader);
            }

            if (descriptor is null)
            {
                throw new MessageDescriptorNotFoundException(
                    messageType,
                    request.MessageResolveStrategy.GetType(),
                    request.RegisterPlainMessagesOnSpot,
                    _messageReader.Count);
            }

            // Resolve handlers from the mediation scope. Without a caller-supplied scope each mediation gets distinct
            // scoped instances; with one, every mediation that shares it resolves the same instances.
            var messageDependencies = new MessageDependencies(messageType,
                descriptor,
                dispatchScope.ServiceProvider,
                request.Tags,
                request.HandlerPredicate);

            // Mediate the message using the specified strategy.
            var result = request.MessageMediationStrategy.Mediate(message, messageDependencies, executionContext);
            executionScope.Dispose();

            if (externalScope is not null)
            {
                return result;
            }

            return MediationScopeRetention.RetainUntilPipelineCompletes(
                result,
                new MediationResourceScope(dispatchScope));
        }
        catch
        {
            executionScope?.Dispose();
            ownedScope?.Dispose();
            throw;
        }
    }

    /// <summary>
    ///     Resolves the identity one mediation runs for, first match wins.
    /// </summary>
    /// <param name="requestIdentity">The identity the caller passed on the mediation request, if any.</param>
    /// <param name="enclosingContext">The execution context of the enclosing mediation, captured before this one replaces it.</param>
    /// <param name="dispatchScope">The dispatch scope the registered identity provider is resolved from.</param>
    /// <returns>
    ///     The identity, resolved on first read; <see cref="ExecutionIdentity.Anonymous" /> when nothing supplies one.
    /// </returns>
    /// <remarks>
    ///     <para>
    ///         Resolution order: the identity passed on the request (the semantic mediators copy it from their
    ///         settings), the identity of the enclosing mediation when mediations nest and it is not
    ///         <see cref="ExecutionIdentity.Anonymous" />, the <see cref="IExecutionIdentityProvider" /> registered in
    ///         the dispatch scope, and finally <see cref="ExecutionIdentity.Anonymous" />.
    ///     </para>
    ///     <para>
    ///         Only the explicit request identity is taken at once. The rest is deferred to the first read, so a
    ///         mediation that never reads its identity does not open the dispatch scope to look for a provider.
    ///     </para>
    /// </remarks>
    private static Lazy<ExecutionIdentity> ResolveIdentity(
        ExecutionIdentity? requestIdentity,
        IExecutionContext? enclosingContext,
        IMessageDispatchScope dispatchScope)
    {
        if (requestIdentity is not null)
        {
            return new Lazy<ExecutionIdentity>(requestIdentity);
        }

        return new Lazy<ExecutionIdentity>(() =>
        {
            var enclosing = enclosingContext?.Identity;

            if (enclosing is not null && !enclosing.Equals(ExecutionIdentity.Anonymous))
            {
                return enclosing;
            }

            if (dispatchScope.ServiceProvider.GetService(typeof(IExecutionIdentityProvider)) is IExecutionIdentityProvider provider)
            {
                return provider.GetCurrentIdentity() ?? ExecutionIdentity.Anonymous;
            }

            return ExecutionIdentity.Anonymous;
        });
    }

    /// <summary>
    ///     Evaluates the registered mediation nesting rules for a nested mediation.
    /// </summary>
    /// <param name="parentContext">The execution context of the enclosing mediation, or <see langword="null" /> for a root mediation.</param>
    /// <param name="messageType">The runtime type of the message being mediated.</param>
    /// <param name="dispatchScope">The dispatch scope the registered nesting rules resolve from.</param>
    /// <exception cref="NestedMediationException">A registered rule denied the nested mediation.</exception>
    /// <remarks>
    ///     Root mediations return before touching the dispatch scope, so a composition without nesting rules pays
    ///     nothing for them. Rules evaluate in registration order and the first denial wins, before the pipeline
    ///     starts, so a denied message never reaches a guard, a handler, or the registry.
    /// </remarks>
    private static void ThrowIfNestingDenied(
        IExecutionContext? parentContext,
        Type messageType,
        IMessageDispatchScope dispatchScope)
    {
        if (parentContext is null)
        {
            return;
        }

        var rules = ResolveNestingRules(dispatchScope.ServiceProvider);

        if (rules.Length == 0)
        {
            return;
        }

        var context = new MediationNestingContext(parentContext, messageType);

        foreach (var rule in rules)
        {
            var verdict = rule.Evaluate(context);

            if (!verdict.IsDenied)
            {
                continue;
            }

            throw new NestedMediationException(
                FindOuterMessageType(context, messageType),
                messageType,
                verdict.Reason!,
                verdict.Code);
        }
    }

    /// <summary>
    ///     Resolves the registered mediation nesting rules from the dispatch scope.
    /// </summary>
    /// <param name="serviceProvider">The service provider nesting rules resolve from.</param>
    /// <returns>The registered rules in registration order, or an empty array when there are none.</returns>
    /// <remarks>
    ///     The collection resolves first, because that is how the command module registers the built-in rule. A single
    ///     rule registered directly against the service type is honored as a one-item collection, so a host
    ///     that wires one by hand without the module builder still contributes.
    /// </remarks>
    private static IMediationNestingRule[] ResolveNestingRules(IServiceProvider serviceProvider)
    {
        if (serviceProvider.GetService(typeof(IEnumerable<IMediationNestingRule>)) is IEnumerable<IMediationNestingRule> rules)
        {
            return rules.ToArray();
        }

        if (serviceProvider.GetService(typeof(IMediationNestingRule)) is IMediationNestingRule rule)
        {
            return [rule];
        }

        return [];
    }

    /// <summary>
    ///     Finds the message type of the nearest enclosing mediation that carries one.
    /// </summary>
    /// <param name="context">The nesting context whose ancestors are walked.</param>
    /// <param name="innerMessageType">The inner message type, reported when no enclosing mediation carries one.</param>
    /// <returns>The outer message type the denial is reported against.</returns>
    /// <remarks>
    ///     The walk normally ends at the direct parent. A custom execution context that does not track its message
    ///     type reports <see langword="null" />, so the walk continues past it; when no enclosing mediation carries a
    ///     type, the inner type is reported instead, and the rule's reason still says which ancestor the rule
    ///     objected to.
    /// </remarks>
    private static Type FindOuterMessageType(MediationNestingContext context, Type innerMessageType)
    {
        foreach (var ancestor in context.Ancestors())
        {
            if (ancestor.MessageType is not null)
            {
                return ancestor.MessageType;
            }
        }

        return innerMessageType;
    }

    /// <summary>
    ///     Registers the runtime message type when the registry covers it only through a base type, an interface, or its
    ///     generic type definition.
    /// </summary>
    /// <param name="messageType">The runtime type of the message being mediated.</param>
    /// <remarks>
    ///     <para>
    ///         Without its own descriptor, a message borrows the descriptor of one registered supertype, which carries only
    ///         that supertype's handlers. A message whose base class and interface both have handlers would lose the
    ///         handlers of one of them, and a closed generic message would lose every open generic handler, because those
    ///         are closed only over registered message types. Registering the runtime type links every handler that
    ///         applies to it, the same way registering it at composition does.
    ///     </para>
    ///     <para>
    ///         Registration happens once per runtime type: the next mediation finds the exact descriptor. A message type
    ///         that no registered message covers is left alone, so a message without handlers fails as before.
    ///     </para>
    /// </remarks>
    private void RegisterWhenCoveredOnlyThroughOtherTypes(Type messageType)
    {
        if (_messageReader.Find(messageType)?.MessageType == messageType)
        {
            return;
        }

        foreach (var descriptor in _messageReader)
        {
            if (Covers(descriptor.MessageType, messageType))
            {
                _messageWriter.Register(messageType);
                return;
            }
        }
    }

    /// <summary>
    ///     Determines whether a registered message type covers a runtime message type.
    /// </summary>
    /// <param name="registeredMessageType">The message type of a registered descriptor.</param>
    /// <param name="messageType">The runtime message type.</param>
    /// <returns>
    ///     <see langword="true" /> when the runtime type is assignable to the registered type, or is constructed from it
    ///     when the registered type is a generic type definition, directly, through a base type, or through an interface.
    /// </returns>
    private static bool Covers(Type registeredMessageType, Type messageType)
    {
        if (registeredMessageType.IsAssignableFrom(messageType))
        {
            return true;
        }

        if (!registeredMessageType.IsGenericTypeDefinition)
        {
            return false;
        }

        for (var current = messageType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == registeredMessageType)
            {
                return true;
            }
        }

        foreach (var implemented in messageType.GetInterfaces())
        {
            if (implemented.IsGenericType && implemented.GetGenericTypeDefinition() == registeredMessageType)
            {
                return true;
            }
        }

        return false;
    }
}
