using System;
using System.Linq;
using System.Reflection;
using LiteBus.Messaging.Abstractions;
using LiteBus.Queries.Abstractions;

namespace LiteBus.Queries.Extensions.MicrosoftDependencyInjection;

public class QueryModuleBuilder
{
    private readonly IMessageRegistry _messageRegistry;

    public QueryModuleBuilder(IMessageRegistry messageRegistry)
    {
        _messageRegistry = messageRegistry;
    }

    public QueryModuleBuilder Register<T>() where T : IRegistrableQueryConstruct
    {
        _messageRegistry.Register(typeof(T));

        return this;
    }

    public QueryModuleBuilder Register(Type type)
    {
        if (!type.IsAssignableTo(typeof(IRegistrableQueryConstruct)))
        {
            throw new NotSupportedException($"The given type '{type.Name}' is not a query construct and cannot be registered.");
        }

        _messageRegistry.Register(type);
        return this;
    }

    public QueryModuleBuilder RegisterFrom(Assembly assembly)
    {
        foreach (var registrableQueryConstruct in assembly.GetTypes().Where(t => t.IsAssignableTo(typeof(IRegistrableQueryConstruct))))
        {
            _messageRegistry.Register(registrableQueryConstruct);
        }

        return this;
    }
}