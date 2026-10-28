using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using LiteBus.Messaging.Abstractions;
using LiteBus.Queries.Abstractions;
using LiteBus.Queries.UnitOfWork;
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
        typeof(IQueryValidator<>),
        typeof(IQueryShortcut<,>),
        typeof(IQueryRefusalMapper<,>),
        typeof(IStreamQueryShortcut<,>),
        typeof(IStreamQueryRefusalMapper<,>),
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
    ///     Gets the unit of work implementation type registered through <c>UseUnitOfWork</c>, or <see langword="null" />
    ///     when the application commits its own way.
    /// </summary>
    /// <remarks>
    ///     The module reads this after the configuration action runs, so it can register the implementation as the
    ///     scoped <see cref="IUnitOfWork" />.
    /// </remarks>
    internal Type? UnitOfWorkType { get; private set; }

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
    ///         <see cref="AuditOutcome.Denied" /> rather than <see cref="AuditOutcome.Failed" />; a refusal from a guard
    ///         is already recorded as a denial without one.
    ///     </para>
    /// </remarks>
    public QueryModuleBuilder EnableAuditing()
    {
        AuditingEnabled = true;
        return Register<QueryAuditCompletionHandler>();
    }

    /// <summary>
    ///     Registers the application's unit of work, committed once per successful query mediation.
    /// </summary>
    /// <typeparam name="TUnitOfWork">The unit of work implementation, constructed by the container once per mediation scope.</typeparam>
    /// <returns>The current <see cref="QueryModuleBuilder" /> instance for method chaining.</returns>
    /// <exception cref="LiteBusConfigurationException"><c>UseUnitOfWork</c> was already called on this builder.</exception>
    /// <remarks>
    ///     <para>
    ///         Registers <typeparamref name="TUnitOfWork" /> as the scoped <see cref="IUnitOfWork" /> and a completion
    ///         handler at <see cref="HandlerPriorities.UnitOfWork" /> that calls <c>CommitAsync</c> only when the
    ///         mediation ended with <see cref="MediationOutcome.Succeeded" />. A denial, a validation failure, a
    ///         shortcut answer, a fault, and a cancellation all end without committing.
    ///     </para>
    ///     <para>
    ///         A commit that throws fails the mediation with that exception: the completion stage suppresses a handler
    ///         fault only when the mediation had already failed, so after a success the fault propagates to the caller
    ///         of <c>QueryAsync</c>.
    ///     </para>
    ///     <para>
    ///         LiteBus never opens or owns the transaction; the implementation does. Calling this twice on one builder
    ///         is a configuration error, because two commits would order by registration sequence with no way to say
    ///         which one the transaction belongs to. Registering it on the command module with a different type is
    ///         allowed, and each axis then commits its own, but prefer one type: the shared <see cref="IUnitOfWork" />
    ///         service resolves to the first module's registration.
    ///     </para>
    /// </remarks>
    public QueryModuleBuilder UseUnitOfWork<TUnitOfWork>()
        where TUnitOfWork : class, IUnitOfWork
    {
        if (UnitOfWorkType is not null)
        {
            throw new LiteBusConfigurationException(
                $"A unit of work is already registered on this query module builder: '{UnitOfWorkType.Name}'. " +
                "Call UseUnitOfWork once per axis; registering it on the command module with a different type is allowed.");
        }

        UnitOfWorkType = typeof(TUnitOfWork);

        return Register<QueryUnitOfWorkCompletionHandler<TUnitOfWork>>();
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

        foreach (var registrableQueryConstruct in AssemblyTypeScanner.GetLoadableTypes(assembly)
                     .Where(static type => type is { IsClass: true, IsAbstract: false } && IsQueryConstruct(type)))
        {
            _messageRegistry.RegisterFromScan(registrableQueryConstruct);
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
            // Both shapes count: the keyed one that types a single declaration, and the describe one that declares
            // several without an explicit interface implementation per value.
            if (contractDefinition == typeof(IMessageDefinition<,>) ||
                contractDefinition == typeof(IMessageDefinition<>))
            {
                return typeof(IQuery).IsAssignableFrom(contract.GetGenericArguments()[0]);
            }

            if (HandlerContracts.Contains(contractDefinition))
            {
                return true;
            }

            // A pipeline handler written against the messaging-level contract counts when its message type is
            // constrained to this axis. Without this, a cross-cutting guard has to be written once per axis, and the
            // code being copied is usually the code least safe to have two copies of.
            return MessagingHandlerContracts.NamesMessageAssignableTo(contract, typeof(IQuery));
        });
    }
}
