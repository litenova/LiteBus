using System;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Contexts.Execution;

namespace LiteBus.Messaging.Mediator;

/// <summary>
///     Implements the <see cref="IMessageMediator" /> interface to provide message mediation capabilities.
/// </summary>
/// <remarks>
///     The <see cref="MessageMediator" /> is responsible for handling the mediation of messages by:
///     <list type="bullet">
///         <li>Creating and managing execution contexts for each mediation operation</li>
///         <li>Resolving message handlers through the message registry</li>
///         <li>Applying the appropriate mediation strategy to process messages</li>
///         <li>Managing nested mediation calls by preserving execution context state</li>
///     </list>
/// </remarks>
internal sealed class MessageMediator : IMessageMediator
{
    private readonly IMessageRegistry _messageRegistry;
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    ///     Initializes a new instance of the <see cref="MessageMediator" /> class.
    /// </summary>
    /// <param name="messageRegistry">The registry containing message handler information.</param>
    /// <param name="serviceProvider">The service provider used to resolve dependencies.</param>
    public MessageMediator(IMessageRegistry messageRegistry,
                           IServiceProvider serviceProvider)
    {
        _messageRegistry = messageRegistry ?? throw new ArgumentNullException(nameof(messageRegistry));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    /// <summary>
    ///     Mediates a message to its appropriate handler and returns the result.
    /// </summary>
    /// <typeparam name="TMessage">The type of the message.</typeparam>
    /// <typeparam name="TMessageResult">The type of the result.</typeparam>
    /// <param name="message">The message to mediate.</param>
    /// <param name="options">The options that control the mediation process.</param>
    /// <returns>The result of the message handling.</returns>
    /// <exception cref="NoHandlerFoundException">
    ///     Thrown when no handler is found for the message type and registration on spot
    ///     is disabled.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when no descriptor can be found for the message type with the
    ///     specified resolve strategy.
    /// </exception>
    public TMessageResult Mediate<TMessage, TMessageResult>(TMessage message,
                                                            MediateOptions<TMessage, TMessageResult> options) where TMessage : notnull
    {
        // Create a new execution context for the current scope
        var executionContext = new ExecutionContext(options.CancellationToken, options.Tags, options.Items);

        // Use a scope to manage the execution context
        using var _ = AmbientExecutionContext.CreateScope(executionContext);

        // Get the actual type of the message
        var messageType = message.GetType();

        // Find the message descriptor
        var descriptor = options.MessageResolveStrategy.Find(messageType, _messageRegistry);

        if (descriptor is null)
        {
            if (!options.RegisterPlainMessagesOnSpot)
            {
                throw new NoHandlerFoundException(messageType);
            }

            _messageRegistry.Register(messageType);

            descriptor = options.MessageResolveStrategy.Find(messageType, _messageRegistry);
        }

        if (descriptor is null)
        {
            throw new InvalidOperationException($"No descriptor found for message type {messageType} with specified resolve strategy.");
        }

        // Resolve the dependencies in lazy mode
        var messageDependencies = new MessageDependencies(messageType,
            descriptor,
            _serviceProvider,
            options.Tags,
            options.HandlerPredicate);

        // Mediate the message using the specified strategy
        return options.MessageMediationStrategy.Mediate(message, messageDependencies, AmbientExecutionContext.Current);
    }
}