using System;
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
    public TMessageResult Mediate<TMessage, TMessageResult>(
        TMessage message,
        MessageMediationRequest<TMessage, TMessageResult> request,
        CancellationToken cancellationToken = default)
        where TMessage : notnull
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(request);

        // Create a new execution context for the current scope.
        var executionContext = new ExecutionContext(request.Tags, request.Items, _timeProvider, cancellationToken);

        // Retain only the dispatch scope until asynchronous mediation results complete. The ambient scope must not leak
        // into the caller after this method returns.
        var executionScope = AmbientExecutionContext.CreateScope(executionContext);
        var dispatchScope = new LazyMessageDispatchScope(_dispatchScopeFactory);

        try
        {
            // Get the actual type of the message.
            var messageType = message.GetType();

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

            // Resolve handlers from the per-mediation scope so scoped handlers get distinct instances.
            var messageDependencies = new MessageDependencies(messageType,
                descriptor,
                dispatchScope.ServiceProvider,
                request.Tags,
                request.HandlerPredicate);

            // Mediate the message using the specified strategy.
            var result = request.MessageMediationStrategy.Mediate(message, messageDependencies, executionContext);
            executionScope.Dispose();

            return MediationScopeRetention.RetainUntilPipelineCompletes(
                result,
                new MediationResourceScope(dispatchScope));
        }
        catch
        {
            executionScope.Dispose();
            dispatchScope.Dispose();
            throw;
        }
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
