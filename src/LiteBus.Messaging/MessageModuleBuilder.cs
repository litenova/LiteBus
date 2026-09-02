using System;
using System.Collections.Generic;
using System.Reflection;
using LiteBus.Messaging.Abstractions;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Messaging;

/// <summary>
///     Configures handler and message type registration for the messaging module.
/// </summary>
public sealed class MessageModuleBuilder
{
    /// <summary>
    ///     Gets the shared message registry used to register handlers and message types.
    /// </summary>
    private readonly IMessageRegistry _messageRegistry;

    /// <summary>
    ///     The metadata value types every registered message must declare or be exempt from, in the order required.
    /// </summary>
    private readonly List<Type> _requiredDeclarations = [];

    /// <summary>
    ///     Initializes a new instance of the <see cref="MessageModuleBuilder" /> class.
    /// </summary>
    /// <param name="messageRegistry">The message registry shared across LiteBus modules.</param>
    /// <param name="contracts">The message contract registry for persisted inbox and outbox messages.</param>
    public MessageModuleBuilder(IMessageRegistry messageRegistry, IContractWriter contracts)
    {
        _messageRegistry = messageRegistry;
        Contracts = contracts;
    }

    /// <summary>
    ///     Gets the message contract writer used to register stable persisted contracts.
    /// </summary>
    public IContractWriter Contracts { get; }

    /// <summary>
    ///     Gets the optional time provider registered for messaging and dependent modules.
    /// </summary>
    internal TimeProvider? TimeProvider { get; private set; }

    /// <summary>
    ///     Gets the metadata value types every registered message must declare or be exempt from.
    /// </summary>
    internal IReadOnlyList<Type> RequiredDeclarations => _requiredDeclarations;

    /// <summary>
    ///     Requires every registered message to declare a value of type <typeparamref name="TValue" /> or to record an
    ///     exemption from it.
    /// </summary>
    /// <typeparam name="TValue">The metadata value type each message must state a position on.</typeparam>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     <para>
    ///         This is what turns a written policy into a startup failure. "Every command states the permission it
    ///         requires" is otherwise enforced by code review, and a command that forgets is an unguarded use case
    ///         nobody notices. Requiring the declaration makes the omission a composition error naming every offender.
    ///     </para>
    ///     <para>
    ///         A message satisfies the requirement by declaring the value through an attribute or a definition, or by
    ///         carrying a <see cref="DeclarationExemptAttribute" /> for that value type. A declaration inherited from a
    ///         base type or marker interface counts, so a family of messages can satisfy it once.
    ///     </para>
    ///     <para>
    ///         The check runs after every module has been built, because the messaging module is foundational and has no
    ///         commands or queries to inspect while it is being built. Abstract types and interfaces are skipped: they
    ///         are shapes rather than messages, and a declaration on one covers the messages beneath it.
    ///     </para>
    ///     <para>
    ///         Analyzer <c>LB1020</c> reports the same omission at compile time, message by message, and is the better
    ///         first line. Keep both: the analyzer catches it while writing the message, and this catches a message
    ///         registered from an assembly the analyzer never saw.
    ///     </para>
    /// </remarks>
    public MessageModuleBuilder RequireDeclaration<TValue>()
        where TValue : notnull
    {
        if (!_requiredDeclarations.Contains(typeof(TValue)))
        {
            _requiredDeclarations.Add(typeof(TValue));
        }

        return this;
    }

    /// <summary>
    ///     Registers the <see cref="TimeProvider" /> instance exposed through dependency injection.
    /// </summary>
    /// <param name="timeProvider">
    ///     The time provider to register. When omitted at build time,
    ///     <see cref="TimeProvider.System" /> is used.
    /// </param>
    /// <returns>The current builder.</returns>
    public MessageModuleBuilder UseTimeProvider(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        TimeProvider = timeProvider;
        return this;
    }

    /// <summary>
    ///     Gets the optional audit outcome mapper exposed through dependency injection.
    /// </summary>
    internal IAuditOutcomeMapper? AuditOutcomeMapper { get; private set; }

    /// <summary>
    ///     Gets the audit trail the module registers, as an instance or an implementation type.
    /// </summary>
    /// <value>
    ///     The instance passed to <see cref="UseAuditTrailInstance" />, the type passed to
    ///     <see cref="UseAuditTrail{TAuditTrail}" />, or <see langword="null" /> when the application registers the trail
    ///     with its own container instead.
    /// </value>
    internal object? AuditTrail { get; private set; }

    /// <summary>
    ///     Gets the lifetime the trail implementation type is registered with.
    /// </summary>
    internal InstanceLifetime AuditTrailLifetime { get; private set; } = InstanceLifetime.Scoped;

    /// <summary>
    ///     Registers the <see cref="IAuditTrail" /> type that receives audit records, constructed by the container.
    /// </summary>
    /// <typeparam name="TAuditTrail">The trail implementation.</typeparam>
    /// <param name="lifetime">
    ///     The lifetime the trail is resolved with. Defaults to <see cref="InstanceLifetime.Scoped" />, which is what a
    ///     trail taking a database session needs.
    /// </param>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     <para>
    ///         This is the one place the audit feature is plumbed: the trail here, the optional mapper through
    ///         <see cref="UseAuditOutcomeMapper" />, and the per-axis switch through <c>EnableAuditing</c> on the command
    ///         or query module, which decides which messages produce records.
    ///     </para>
    ///     <para>
    ///         The lifetime is a parameter rather than a consequence of which overload was reached for. A trail wrapping
    ///         a scoped database session is correct as <see cref="InstanceLifetime.Scoped" /> and captures one session
    ///         for the life of the process as a singleton, and nothing about the call site used to say which one you
    ///         were choosing.
    ///     </para>
    ///     <para>
    ///         Registering the trail with the application container instead still works, and the
    ///         <c>litebus.audit.trail</c> diagnostic check accepts either.
    ///     </para>
    /// </remarks>
    public MessageModuleBuilder UseAuditTrail<TAuditTrail>(InstanceLifetime lifetime = InstanceLifetime.Scoped)
        where TAuditTrail : class, IAuditTrail
    {
        AuditTrail = typeof(TAuditTrail);
        AuditTrailLifetime = lifetime;
        return this;
    }

    /// <summary>
    ///     Registers a pre-created <see cref="IAuditTrail" /> that receives audit records.
    /// </summary>
    /// <param name="auditTrail">The trail instance, shared by every mediation for the life of the process.</param>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     The name says the lifetime, because a pre-created instance can only be a singleton. A trail built here with a
    ///     database session captures that one session forever, which is the failure this name exists to make visible at
    ///     the call site. Use <see cref="UseAuditTrail{TAuditTrail}" /> whenever the trail has dependencies.
    /// </remarks>
    public MessageModuleBuilder UseAuditTrailInstance(IAuditTrail auditTrail)
    {
        ArgumentNullException.ThrowIfNull(auditTrail);
        AuditTrail = auditTrail;
        AuditTrailLifetime = InstanceLifetime.Singleton;
        return this;
    }

    /// <summary>
    ///     Registers the <see cref="IAuditOutcomeMapper" /> instance used to classify how an audited action ended.
    /// </summary>
    /// <param name="auditOutcomeMapper">The mapper to register, shared for the life of the process.</param>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     <para>
    ///         LiteBus knows that a mediation failed but cannot know whether it failed because the actor was not
    ///         permitted. Register a mapper so that an application refusal exception is recorded as
    ///         <see cref="AuditOutcome.Denied" /> rather than <see cref="AuditOutcome.Failed" />. An application that
    ///         refuses through a guard or a validator needs no mapper: the pipeline already reports
    ///         <see cref="MediationOutcome.Denied" /> and <see cref="MediationOutcome.Invalid" /> as decisions.
    ///     </para>
    ///     <para>
    ///         When omitted, <c>DefaultAuditOutcomeMapper</c> maps each mediation outcome to the audit outcome that
    ///         matches it and records every remaining failure as <see cref="AuditOutcome.Failed" />.
    ///     </para>
    /// </remarks>
    public MessageModuleBuilder UseAuditOutcomeMapper(IAuditOutcomeMapper auditOutcomeMapper)
    {
        ArgumentNullException.ThrowIfNull(auditOutcomeMapper);
        AuditOutcomeMapper = auditOutcomeMapper;
        return this;
    }

    /// <summary>
    ///     Registers the <see cref="IAuditOutcomeMapper" /> used to classify how an audited action ended.
    /// </summary>
    /// <typeparam name="TAuditOutcomeMapper">The mapper implementation, constructed once at configuration time.</typeparam>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     The mapper is read on the completion path of every audited mediation and must stay pure, so it is created
    ///     once here rather than resolved per scope. A mapper needing scoped state is reading the wrong thing: the
    ///     completion context it is handed already carries the outcome, the exception, and the reason.
    /// </remarks>
    public MessageModuleBuilder UseAuditOutcomeMapper<TAuditOutcomeMapper>()
        where TAuditOutcomeMapper : IAuditOutcomeMapper, new()
    {
        AuditOutcomeMapper = new TAuditOutcomeMapper();
        return this;
    }

    /// <summary>
    ///     Registers a message or handler type with the message registry.
    /// </summary>
    /// <typeparam name="T">The type to register.</typeparam>
    /// <returns>The current builder.</returns>
    public MessageModuleBuilder Register<T>()
    {
        _messageRegistry.Register(typeof(T));

        return this;
    }

    /// <summary>
    ///     Registers a message or handler type with the message registry.
    /// </summary>
    /// <param name="type">The type to register.</param>
    /// <returns>The current builder.</returns>
    public MessageModuleBuilder Register(Type type)
    {
        _messageRegistry.Register(type);
        return this;
    }

    /// <summary>
    ///     Registers all applicable types from an assembly with the message registry.
    /// </summary>
    /// <param name="assembly">The assembly to scan.</param>
    /// <returns>The current builder.</returns>
    public MessageModuleBuilder RegisterFromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        foreach (var type in assembly.GetTypes())
        {
            _messageRegistry.Register(type);
        }

        Contracts.AddFromAssembly(assembly);

        return this;
    }
}