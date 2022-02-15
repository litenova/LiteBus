using System;
using System.Reflection;
using LiteBus.Messaging.Abstractions;
using LiteBus.Queries.Abstractions;

namespace LiteBus.Queries.Extensions.MicrosoftDependencyInjection;

public class LiteBusQueryBuilder
{
    private readonly IMessageRegistry _messageRegistry;

    public LiteBusQueryBuilder(IMessageRegistry messageRegistry)
    {
        _messageRegistry = messageRegistry;
    }

    /// <summary>
    ///     Register a query or query handler (i.e., handlers, pre-handlers, post-handlers, error-handlers)
    /// </summary>
    /// <typeparam name="T">The type of query or query handler</typeparam>
    /// <returns>The instance of <see cref="LiteBusQueryBuilder" /></returns>
    /// <exception cref="NotSupportedException">In case the given type is neither query nor query handler</exception>
    public LiteBusQueryBuilder Register<T>()
    {
        return Register(typeof(T));
    }

    /// <summary>
    ///     Register a query or query handler (i.e., handlers, pre-handlers, post-handlers, error-handlers)
    /// </summary>
    /// <param name="type">The type of query or query handler</param>
    /// <returns>The instance of <see cref="LiteBusQueryBuilder" /></returns>
    /// <exception cref="NotSupportedException">In case the given type is neither query nor query handler</exception>
    public LiteBusQueryBuilder Register(Type type)
    {
        if (type.IsAssignableTo(typeof(IQueryHandler)) || type.IsAssignableTo(typeof(IQuery)))
        {
            _messageRegistry.Register(type);
            return this;
        }

        throw new NotSupportedException($"The type of '{type.Name}' cannot be registered");
    }

    /// <summary>
    ///     Registers queries and query handlers found in the given assembly
    /// </summary>
    /// <param name="assembly">The assembly to search</param>
    /// <returns>The instance of <see cref="LiteBusQueryBuilder" /></returns>
    public LiteBusQueryBuilder RegisterFrom(Assembly assembly)
    {
        foreach (var type in assembly.GetTypes())
        {
            if (type.IsAssignableTo(typeof(IQueryHandler)) || type.IsAssignableTo(typeof(IQuery)))
            {
                _messageRegistry.Register(type);
            }
        }

        return this;
    }
}