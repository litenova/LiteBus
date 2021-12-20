using System.Reflection;
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Extensions;

namespace LiteBus.Commands.Extensions.MicrosoftDependencyInjection;

public class LiteBusCommandBuilder
{
    private readonly IMessageRegistry _messageRegistry;

    public LiteBusCommandBuilder(IMessageRegistry messageRegistry)
    {
        _messageRegistry = messageRegistry;
    }

    public LiteBusCommandBuilder RegisterFrom(Assembly assembly)
    {
        _messageRegistry.RegisterFrom<ICommandConstruct>(assembly);
        return this;
    }

    public LiteBusCommandBuilder RegisterHandler<THandler>() where THandler : ICommandHandlerBase
    {
        _messageRegistry.Register(typeof(THandler));

        return this;
    }

    public LiteBusCommandBuilder RegisterPreHandler<TCommandPreHandler>()
        where TCommandPreHandler : ICommandPreHandlerBase
    {
        _messageRegistry.Register(typeof(TCommandPreHandler));

        return this;
    }

    public LiteBusCommandBuilder RegisterPostHandler<TCommandPostHandler>()
        where TCommandPostHandler : ICommandPostHandlerBase
    {
        _messageRegistry.Register(typeof(TCommandPostHandler));

        return this;
    }

    public LiteBusCommandBuilder RegisterErrorHandler<TCommandErrorHandler>()
        where TCommandErrorHandler : ICommandErrorHandlerBase
    {
        _messageRegistry.Register(typeof(TCommandErrorHandler));

        return this;
    }
}