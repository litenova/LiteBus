using System;
using System.Collections.Generic;
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
    ///     Query handler contracts accepted by query-specific discovery.
    /// </summary>
    private static readonly HashSet<Type> HandlerContracts =
    [
        typeof(IQueryHandler<,>),
        typeof(IQueryPreHandler),
        typeof(IQueryPreHandler<>),
        typeof(IQueryPostHandler),
        typeof(IQueryPostHandler<>),
        typeof(IQueryPostHandler<,>),
        typeof(IQueryErrorHandler),
        typeof(IQueryErrorHandler<>),
        typeof(IQueryErrorHandler<,>),
        typeof(IStreamQueryHandler<,>),
        typeof(IStreamQueryPostHandler<,>)
    ];

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
    /// <typeparam name="T">The query or query handler type to register.</typeparam>
    /// <returns>The current <see cref="QueryModuleBuilder" /> instance for method chaining.</returns>
    public QueryModuleBuilder Register<T>()
    {
        return Register(typeof(T));
    }

    /// <summary>
    ///     Registers a query type for the message registry.
    /// </summary>
    /// <param name="type">The query or query handler type to register.</param>
    /// <returns>The current <see cref="QueryModuleBuilder" /> instance for method chaining.</returns>
    public QueryModuleBuilder Register(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        if (!IsQueryConstruct(type))
        {
            throw new LiteBusNotSupportedException($"The given type '{type.Name}' is not a query construct and cannot be registered.");
        }

        _messageRegistry.Register(type);
        return this;
    }

    /// <summary>
    ///     Registers all concrete query and query handler types from the specified assembly.
    /// </summary>
    /// <param name="assembly">The assembly from which to register query types.</param>
    /// <returns>The current <see cref="QueryModuleBuilder" /> instance for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="assembly" /> is <see langword="null" />.</exception>
    public QueryModuleBuilder RegisterFromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        foreach (var registrableQueryConstruct in assembly.GetTypes()
                     .Where(static type => type is { IsClass: true, IsAbstract: false } && IsQueryConstruct(type)))
        {
            _messageRegistry.Register(registrableQueryConstruct);
        }

        return this;
    }

    /// <summary>
    ///     Determines whether a type is a query message or implements a query handler contract.
    /// </summary>
    /// <param name="type">The candidate type.</param>
    /// <returns><see langword="true" /> when the type belongs to the query axis; otherwise, <see langword="false" />.</returns>
    internal static bool IsQueryConstruct(Type type)
    {
        if (typeof(IQuery).IsAssignableFrom(type))
        {
            return true;
        }

        return type.GetInterfaces().Any(static contract =>
        {
            var contractDefinition = contract.IsGenericType ? contract.GetGenericTypeDefinition() : contract;
            return HandlerContracts.Contains(contractDefinition);
        });
    }
}
