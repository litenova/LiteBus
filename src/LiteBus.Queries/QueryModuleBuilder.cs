using System;
using System.Linq;
using System.Reflection;
using LiteBus.Messaging.Abstractions;
using LiteBus.Queries.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Queries;

/// <summary>
///     Builder class for registering query types in the message registry.
/// </summary>
public sealed class QueryModuleBuilder
{
    /// <summary>
    ///     Gets the message registry to which query types are registered.
    /// </summary>
    private readonly IMessageRegistry _messageRegistry;

    /// <summary>
    ///     Initializes a new instance of the <see cref="QueryModuleBuilder" /> class.
    /// </summary>
    /// <param name="messageRegistry">The message registry to which queries will be registered.</param>
    /// <param name="contracts">The contract writer used during module configuration.</param>
    public QueryModuleBuilder(IMessageRegistry messageRegistry, IContractWriter contracts)
    {
        _messageRegistry = messageRegistry;
        ArgumentNullException.ThrowIfNull(contracts);
        Contracts = contracts;
    }

    /// <summary>
    ///     Gets the message contract writer for persisted query contracts.
    /// </summary>
    public IContractWriter Contracts { get; }

    /// <summary>
    ///     Registers a query type for the message registry.
    /// </summary>
    /// <typeparam name="T">The type of query to register, which must implement <see cref="IRegistrableQueryConstruct" />.</typeparam>
    /// <returns>The current <see cref="QueryModuleBuilder" /> instance for method chaining.</returns>
    public QueryModuleBuilder Register<T>() where T : IRegistrableQueryConstruct
    {
        _messageRegistry.Register(typeof(T));
        return this;
    }

    /// <summary>
    ///     Registers a query type for the message registry.
    /// </summary>
    /// <param name="type">The type of query to register, which must implement <see cref="IRegistrableQueryConstruct" />.</param>
    /// <returns>The current <see cref="QueryModuleBuilder" /> instance for method chaining.</returns>
    public QueryModuleBuilder Register(Type type)
    {
        if (!type.IsAssignableTo(typeof(IRegistrableQueryConstruct)))
        {
            throw new LiteBusNotSupportedException($"The given type '{type.Name}' is not a query construct and cannot be registered.");
        }

        _messageRegistry.Register(type);
        return this;
    }

    /// <summary>
    ///     Registers all concrete query constructs from the specified assembly that implement
    ///     <see cref="IRegistrableQueryConstruct" />.
    /// </summary>
    /// <param name="assembly">The assembly from which to register query types.</param>
    /// <returns>The current <see cref="QueryModuleBuilder" /> instance for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="assembly" /> is <see langword="null" />.</exception>
    public QueryModuleBuilder RegisterFromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        foreach (var registrableQueryConstruct in assembly.GetTypes()
                     .Where(t => t is { IsClass: true, IsAbstract: false } && t.IsAssignableTo(typeof(IRegistrableQueryConstruct))))
        {
            _messageRegistry.Register(registrableQueryConstruct);
        }

        return this;
    }
}