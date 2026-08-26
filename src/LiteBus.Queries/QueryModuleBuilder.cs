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
        typeof(IQueryGuard<>),
        typeof(IQueryGuard<,>),
        typeof(IStreamQueryGuard<,>),
        typeof(IQueryShortcut<,>),
        typeof(IStreamQueryShortcut<,>),
        typeof(IQueryPostHandler),
        typeof(IQueryPostHandler<>),
        typeof(IQueryPostHandler<,>),
        typeof(IQueryErrorHandler),
        typeof(IQueryErrorHandler<>),
        typeof(IQueryErrorHandler<,>),
        typeof(IStreamQueryHandler<,>),
        typeof(IStreamQueryPostHandler<,>),
        typeof(IQueryCompletionHandler),
        typeof(IQueryCompletionHandler<>),
        typeof(IQueryCompletionHandler<,>)
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
    ///     Gets a value indicating whether <see cref="EnableAuditing" /> was called.
    /// </summary>
    /// <remarks>
    ///     The module reads this after the configuration action runs, so it can register the diagnostic probe that reports
    ///     a missing <see cref="IAuditTrail" /> before the first audited mediation fails inside the completion stage.
    /// </remarks>
    internal bool AuditingEnabled { get; private set; }

    /// <summary>
    ///     Registers the LiteBus query audit writer, so every query mediation produces an audit record when the
    ///     message declares one.
    /// </summary>
    /// <returns>The current <see cref="QueryModuleBuilder" /> instance for method chaining.</returns>
    /// <remarks>
    ///     <para>
    ///         The writer runs at the completion stage, so refusals, failures and cancellations are recorded as well as
    ///         successes. A message is recorded only when it declares an audited position through
    ///         <see cref="AuditedAttribute" /> or an <c>IAuditDefinition&lt;TMessage&gt;</c>.
    ///     </para>
    ///     <para>
    ///         The application must register an <see cref="IAuditTrail" /> implementation; the
    ///         <c>litebus.audit.trail</c> diagnostic probe reports when it is missing. Registering an
    ///         <see cref="IAuditOutcomeMapper" /> is optional and lets a refusal raised as an exception be recorded as
    ///         <see cref="AuditOutcome.Denied" /> rather than <see cref="AuditOutcome.Failed" />; a refusal from a gate
    ///         is already recorded as a denial without one.
    ///     </para>
    /// </remarks>
    public QueryModuleBuilder EnableAuditing()
    {
        AuditingEnabled = true;
        return Register<QueryAuditCompletionHandler>();
    }

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
            if (!contract.IsGenericType)
            {
                return HandlerContracts.Contains(contract);
            }

            var contractDefinition = contract.GetGenericTypeDefinition();

            // A message definition declares metadata for a query rather than implementing a handler contract.
            if (contractDefinition == typeof(IMessageDefinition<,>))
            {
                return typeof(IQuery).IsAssignableFrom(contract.GetGenericArguments()[0]);
            }

            return HandlerContracts.Contains(contractDefinition);
        });
    }
}
