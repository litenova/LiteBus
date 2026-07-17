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
    ///     Initializes a new instance of the <see cref="MessageMediator" /> class.
    /// </summary>
    /// <param name="messageReader">The reader containing message handler information.</param>
    /// <param name="messageWriter">The writer used for on-the-spot message registration.</param>
    /// <param name="dispatchScopeFactory">The factory that creates per-mediation dependency injection scopes.</param>
    public MessageMediator(
        IMessageReader messageReader,
        IMessageWriter messageWriter,
        IMessageDispatchScopeFactory dispatchScopeFactory)
    {
        ArgumentNullException.ThrowIfNull(messageReader);
        _messageReader = messageReader;
        ArgumentNullException.ThrowIfNull(messageWriter);
        _messageWriter = messageWriter;
        ArgumentNullException.ThrowIfNull(dispatchScopeFactory);
        _dispatchScopeFactory = dispatchScopeFactory;
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
    public TMessageResult Mediate<TMessage, TMessageResult>(
        TMessage message,
        MessageMediationRequest<TMessage, TMessageResult> request,
        CancellationToken cancellationToken = default)
        where TMessage : notnull
    {
        // Create a new execution context for the current scope.
        var executionContext = new ExecutionContext(request.Tags, request.Items, cancellationToken);

        // Retain ambient and dispatch scopes until asynchronous mediation results complete.
        var executionScope = AmbientExecutionContext.CreateScope(executionContext);
        var dispatchScope = _dispatchScopeFactory.CreateScope();
        var resourceScope = new MediationResourceScope(executionScope, dispatchScope);

        try
        {
            // Get the actual type of the message.
            var messageType = message.GetType();

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

            return MediationScopeRetention.RetainUntilPipelineCompletes(result, resourceScope);
        }
        catch
        {
            resourceScope.Dispose();
            throw;
        }
    }

}
