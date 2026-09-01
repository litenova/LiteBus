using System;
using System.Reflection;
using LiteBus.Messaging.Abstractions;

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
    ///     The instance passed to <see cref="UseAuditTrail(IAuditTrail)" />, the type passed to
    ///     <see cref="UseAuditTrail{TAuditTrail}" />, or <see langword="null" /> when the application registers the trail
    ///     with its own container instead.
    /// </value>
    internal object? AuditTrail { get; private set; }

    /// <summary>
    ///     Registers the <see cref="IAuditTrail" /> that receives audit records.
    /// </summary>
    /// <typeparam name="TAuditTrail">The trail implementation, resolved per mediation scope.</typeparam>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     <para>
    ///         This is the one place the audit feature is plumbed: the trail here, the optional mapper through
    ///         <see cref="UseAuditOutcomeMapper{TAuditOutcomeMapper}" />, and the per-axis switch through
    ///         <c>EnableAuditing</c> on the command or query module, which decides which messages produce records.
    ///     </para>
    ///     <para>
    ///         Registering the trail with the application container instead still works, and the
    ///         <c>litebus.audit.trail</c> diagnostic check accepts either. Prefer this overload when the trail takes
    ///         dependencies of its own, since the container constructs it.
    ///     </para>
    /// </remarks>
    public MessageModuleBuilder UseAuditTrail<TAuditTrail>()
        where TAuditTrail : class, IAuditTrail
    {
        AuditTrail = typeof(TAuditTrail);
        return this;
    }

    /// <summary>
    ///     Registers a pre-created <see cref="IAuditTrail" /> that receives audit records.
    /// </summary>
    /// <param name="auditTrail">The trail instance, shared by every mediation.</param>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     Use <see cref="UseAuditTrail{TAuditTrail}" /> when the trail takes dependencies, so the container builds it.
    /// </remarks>
    public MessageModuleBuilder UseAuditTrail(IAuditTrail auditTrail)
    {
        ArgumentNullException.ThrowIfNull(auditTrail);
        AuditTrail = auditTrail;
        return this;
    }

    /// <summary>
    ///     Registers the <see cref="IAuditOutcomeMapper" /> used to classify how an audited action ended.
    /// </summary>
    /// <param name="auditOutcomeMapper">The mapper to register.</param>
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