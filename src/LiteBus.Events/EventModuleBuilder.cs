using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using LiteBus.Events.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Events;

/// <summary>
///     Builder class for registering event types in the message registry.
/// </summary>
public sealed class EventModuleBuilder
{
    /// <summary>
    ///     Event handler contracts accepted by event-specific discovery.
    /// </summary>
    private static readonly HashSet<Type> HandlerContracts =
    [
        typeof(IEventHandler<>),
        typeof(IEventPreHandler),
        typeof(IEventPreHandler<>),
        typeof(IEventGuard<>),
        typeof(IEventValidator<>),
        typeof(IEventShortcut<>),
        typeof(IEventPostHandler),
        typeof(IEventPostHandler<>),
        typeof(IEventErrorHandler),
        typeof(IEventErrorHandler<>),
        typeof(IEventCompletionHandler),
        typeof(IEventCompletionHandler<>)
    ];

    /// <summary>
    ///     Gets the message registry to which event types are registered.
    /// </summary>
    private readonly IMessageRegistry _messageRegistry;

    /// <summary>
    ///     Initializes a new instance of the <see cref="EventModuleBuilder" /> class.
    /// </summary>
    /// <param name="messageRegistry">The message registry to which events will be registered.</param>
    /// <param name="contracts">The contract writer used during module configuration.</param>
    public EventModuleBuilder(IMessageRegistry messageRegistry, IContractWriter contracts)
    {
        _messageRegistry = messageRegistry;
        ArgumentNullException.ThrowIfNull(contracts);
        Contracts = contracts;
    }

    /// <summary>
    ///     Gets the message contract writer for persisted event contracts.
    /// </summary>
    public IContractWriter Contracts { get; }

    /// <summary>
    ///     Gets a value indicating whether <see cref="EnableAuditing" /> was called.
    /// </summary>
    /// <remarks>
    ///     The module reads this after the configuration action runs, so it can register the diagnostic probe that
    ///     reports a missing <see cref="IAuditTrail" /> before the first audited publish fails inside the completion
    ///     stage.
    /// </remarks>
    internal bool AuditingEnabled { get; private set; }

    /// <summary>
    ///     Registers the LiteBus event audit writer, so every event mediation produces an audit record when the message
    ///     declares one.
    /// </summary>
    /// <returns>The current <see cref="EventModuleBuilder" /> instance for method chaining.</returns>
    /// <remarks>
    ///     <para>
    ///         A domain fact is frequently the thing a review most wants recorded, so the event axis carries the same
    ///         switch the command and query axes do. A message is recorded only when it declares an audited position
    ///         through <see cref="AuditedAttribute" /> or an <c>IAuditDefinition&lt;TMessage&gt;</c>.
    ///     </para>
    ///     <para>
    ///         One record per publish, not per handler: the mediation is the unit being audited, and a record per
    ///         subscriber would turn one fact into as many entries as there happen to be reactions.
    ///     </para>
    /// </remarks>
    public EventModuleBuilder EnableAuditing()
    {
        AuditingEnabled = true;
        return Register<EventAuditCompletionHandler>();
    }

    /// <summary>
    ///     Registers an event type for the message registry.
    /// </summary>
    /// <typeparam name="T">The event or event handler type to register.</typeparam>
    /// <returns>The current <see cref="EventModuleBuilder" /> instance for method chaining.</returns>
    public EventModuleBuilder Register<T>()
    {
        return Register(typeof(T));
    }

    /// <summary>
    ///     Registers an event type for the message registry.
    /// </summary>
    /// <param name="type">The event or event handler type to register.</param>
    /// <returns>The current <see cref="EventModuleBuilder" /> instance for method chaining.</returns>
    public EventModuleBuilder Register(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        if (!IsEventConstruct(type))
        {
            throw new LiteBusNotSupportedException($"The given type '{type.Name}' is not an event construct and cannot be registered.");
        }

        _messageRegistry.Register(type);
        return this;
    }

    /// <summary>
    ///     Registers all concrete event and event handler types from the specified assembly.
    /// </summary>
    /// <param name="assembly">The assembly from which to register event types.</param>
    /// <returns>The current <see cref="EventModuleBuilder" /> instance for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="assembly" /> is <see langword="null" />.</exception>
    public EventModuleBuilder RegisterFromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var candidates = assembly.GetTypes()
            .Where(static type => type is { IsClass: true, IsAbstract: false })
            .ToList();

        var pocoEventTypes = CollectPocoEventTypes(candidates);

        foreach (var registrableEventConstruct in candidates.Where(type => IsEventConstruct(type) || DeclaresFor(type, pocoEventTypes)))
        {
            _messageRegistry.RegisterFromScan(registrableEventConstruct);
        }

        return this;
    }

    /// <summary>
    ///     Collects the unmarked message types the assembly handles as events.
    /// </summary>
    /// <param name="candidates">The concrete types found in the assembly being scanned.</param>
    /// <returns>The set of message types named by an <see cref="IEventHandler{TEvent}" /> that do not implement <see cref="IEvent" />.</returns>
    /// <remarks>
    ///     A type with no LiteBus marker is an event because something handles it as one. That is the only evidence
    ///     available for a POCO event, and it is the same evidence the handler registration itself relies on.
    /// </remarks>
    private static HashSet<Type> CollectPocoEventTypes(IEnumerable<Type> candidates)
    {
        var pocoEventTypes = new HashSet<Type>();

        foreach (var contract in candidates.SelectMany(static type => type.GetInterfaces()))
        {
            if (!contract.IsGenericType || contract.GetGenericTypeDefinition() != typeof(IEventHandler<>))
            {
                continue;
            }

            var eventType = contract.GetGenericArguments()[0];

            if (!typeof(IEvent).IsAssignableFrom(eventType))
            {
                pocoEventTypes.Add(eventType);
            }
        }

        return pocoEventTypes;
    }

    /// <summary>
    ///     Determines whether a type declares metadata for one of the assembly's unmarked event types.
    /// </summary>
    /// <param name="type">The candidate type.</param>
    /// <param name="pocoEventTypes">The unmarked event types the assembly handles.</param>
    /// <returns><see langword="true" /> when the type is a message definition over one of those events.</returns>
    /// <remarks>
    ///     Without this, a POCO event can be handled, guarded and validated but cannot carry a declaration, so the
    ///     axis would accept the handler and reject the audit or idempotency metadata that belongs beside it.
    /// </remarks>
    private static bool DeclaresFor(Type type, HashSet<Type> pocoEventTypes)
    {
        if (pocoEventTypes.Count == 0)
        {
            return false;
        }

        return type.GetInterfaces().Any(contract =>
        {
            if (!contract.IsGenericType)
            {
                return false;
            }

            var contractDefinition = contract.GetGenericTypeDefinition();

            return (contractDefinition == typeof(IMessageDefinition<,>) || contractDefinition == typeof(IMessageDefinition<>))
                   && pocoEventTypes.Contains(contract.GetGenericArguments()[0]);
        });
    }

    /// <summary>
    ///     Determines whether a type is an event message or implements an event handler contract.
    /// </summary>
    /// <param name="type">The candidate type.</param>
    /// <returns><see langword="true" /> when the type belongs to the event axis; otherwise, <see langword="false" />.</returns>
    internal static bool IsEventConstruct(Type type)
    {
        if (typeof(IEvent).IsAssignableFrom(type))
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

            // A message definition declares metadata for an event rather than implementing a handler contract.
            // Both shapes count: the keyed one that types a single declaration, and the describe one that declares
            // several without an explicit interface implementation per value.
            if (contractDefinition == typeof(IMessageDefinition<,>) ||
                contractDefinition == typeof(IMessageDefinition<>))
            {
                return typeof(IEvent).IsAssignableFrom(contract.GetGenericArguments()[0]);
            }

            if (HandlerContracts.Contains(contractDefinition))
            {
                return true;
            }

            // A pipeline handler written against the messaging-level contract counts when its message type is
            // constrained to this axis. Without this, a cross-cutting guard has to be written once per axis, and the
            // code being copied is usually the code least safe to have two copies of.
            return MessagingHandlerContracts.NamesMessageAssignableTo(contract, typeof(IEvent));
        });
    }
}
