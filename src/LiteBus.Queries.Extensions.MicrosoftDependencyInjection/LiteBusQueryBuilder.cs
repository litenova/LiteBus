using System;
using System.Reflection;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Extensions;
using LiteBus.Queries.Abstractions;

namespace LiteBus.Queries.Extensions.MicrosoftDependencyInjection;

public class LiteBusQueryBuilder
{
    private readonly IMessageRegistry _messageRegistry;

    public LiteBusQueryBuilder(IMessageRegistry messageRegistry)
    {
        _messageRegistry = messageRegistry;
    }

    public LiteBusQueryBuilder Register<TQuery>() where TQuery : IQueryBase
    {
        _messageRegistry.Register(typeof(TQuery));
        return this;
    }

    public LiteBusQueryBuilder RegisterFrom(Assembly assembly)
    {
        _messageRegistry.RegisterFrom<IQueryConstruct>(assembly);
        return this;
    }

    public LiteBusQueryBuilder RegisterHandler<THandler>() where THandler : IQueryHandlerBase
    {
        _messageRegistry.Register(typeof(THandler));

        return this;
    }

    public LiteBusQueryBuilder RegisterHandler(Type type)
    {
        _messageRegistry.Register(type);

        return this;
    }

    public LiteBusQueryBuilder RegisterPreHandler<TQueryPreHandler>()
        where TQueryPreHandler : IQueryPreHandlerBase
    {
        _messageRegistry.Register(typeof(TQueryPreHandler));

        return this;
    }

    public LiteBusQueryBuilder RegisterPreHandler(Type type)
    {
        _messageRegistry.Register(type);

        return this;
    }

    public LiteBusQueryBuilder RegisterPostHandler<TQueryPostHandler>()
        where TQueryPostHandler : IQueryPostHandlerBase
    {
        _messageRegistry.Register(typeof(TQueryPostHandler));

        return this;
    }

    public LiteBusQueryBuilder RegisterPostHandler(Type type)
    {
        _messageRegistry.Register(type);

        return this;
    }

    public LiteBusQueryBuilder RegisterErrorHandler<TQueryErrorHandler>()
    {
        _messageRegistry.Register(typeof(TQueryErrorHandler));

        return this;
    }

    public LiteBusQueryBuilder RegisterErrorHandler(Type type)
    {
        _messageRegistry.Register(type);

        return this;
    }
}