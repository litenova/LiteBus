using System;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging.Internal.Mediator;

internal class MessageMediator : IMessageMediator
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
                                                            IDiscoveryWorkflow discovery,
                                                            IExecutionWorkflow<TMessage, TMessageResult>
                                                                execution)
    {
        var messageType = message.GetType();

        var descriptor = discovery.Discover(_messageRegistry, messageType);

        var context = new MessageContext(messageType, descriptor, _serviceProvider);

        return execution.Execute(message, context);
    }
}