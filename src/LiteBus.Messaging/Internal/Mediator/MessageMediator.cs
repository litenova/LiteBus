using System;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Internal.Contexts.Execution;

#nullable enable

namespace LiteBus.Messaging.Internal.Mediator;

internal sealed class MessageMediator : IMessageMediator
{
    private readonly IMessageRegistry _messageRegistry;
    private readonly IServiceProvider _serviceProvider;

    public MessageMediator(IMessageRegistry messageRegistry,
                           IServiceProvider serviceProvider)
    {
        _messageRegistry = messageRegistry;
        _serviceProvider = serviceProvider;
    }

    public TMessageResult Mediate<TMessage, TMessageResult>(TMessage message,
                                                            MediateOptions<TMessage, TMessageResult> options) where TMessage : notnull
    {
        // In case we are in a nested call, we need to save the original execution context
        var originalExecutionContext = AmbientExecutionContext.Current;

        // Create a new execution context for the current scope
        AmbientExecutionContext.Current = new ExecutionContext(options.CancellationToken, options.Tags);

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
            else
            {
                _messageRegistry.Register(messageType);
                descriptor = options.MessageResolveStrategy.Find(messageType, _messageRegistry);
            }
        }

        // resolve the dependencies in lazy mode
        var messageDependencies = new MessageDependencies(messageType, descriptor, _serviceProvider, options.Tags);

        var result = options.MessageMediationStrategy.Mediate(message, messageDependencies, AmbientExecutionContext.Current);

        // Restore the original execution context when the nested call is finished
        AmbientExecutionContext.Current = originalExecutionContext;

        return result;
    }
}