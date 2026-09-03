using System;
using LiteBus.Messaging.Abstractions;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Messaging;

/// <summary>
///     Configures the whole audit trail feature in one call: where records go, who they are attributed to, how an
///     outcome is classified, and which axes produce them.
/// </summary>
/// <remarks>
///     <para>
///         Reached through <c>MessageModuleBuilder.AddAuditing</c>. Auditing has one plumbed half and one per-axis
///         half, and configuring them on separate builders let a consumer register a trail with no axis enabled, or an
///         axis with no trail, and discover it from a diagnostic probe rather than from the call site. One builder
///         makes it one decision that cannot be half-made.
///     </para>
///     <para>
///         The per-axis switches on <c>CommandModuleBuilder</c>, <c>QueryModuleBuilder</c> and
///         <c>EventModuleBuilder</c> remain, and are what this composes. Reach for them directly only when the axes are
///         configured in genuinely separate places.
///     </para>
/// </remarks>
/// <example>
///     <code><![CDATA[
/// registry.AddMessaging(messaging => messaging.AddAuditing(auditing => auditing
///     .UseTrail<MartenAuditTrail>(InstanceLifetime.Scoped)
///     .UseActorResolver<RequestActorResolver>()
///     .ForCommands()
///     .ForQueries()));
/// ]]></code>
/// </example>
public sealed class AuditingBuilder
{
    /// <summary>
    ///     The axis selection this builder writes, read by the axis modules as they build.
    /// </summary>
    private readonly Audit.AuditingComposition _composition;

    /// <summary>
    ///     The messaging module builder that owns the trail, resolver, and mapper registrations.
    /// </summary>
    private readonly MessageModuleBuilder _messaging;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AuditingBuilder" /> class.
    /// </summary>
    /// <param name="messaging">The messaging module builder that receives the registrations.</param>
    /// <param name="composition">The axis selection shared with the axis modules.</param>
    internal AuditingBuilder(MessageModuleBuilder messaging, Audit.AuditingComposition composition)
    {
        _messaging = messaging;
        _composition = composition;
    }

    /// <summary>
    ///     Registers the <see cref="IAuditTrail" /> that receives audit records, constructed by the container.
    /// </summary>
    /// <typeparam name="TAuditTrail">The trail implementation.</typeparam>
    /// <param name="lifetime">
    ///     The lifetime the trail is resolved with. Defaults to <see cref="InstanceLifetime.Scoped" />, which is what a
    ///     trail taking a database session needs.
    /// </param>
    /// <returns>The current builder.</returns>
    public AuditingBuilder UseTrail<TAuditTrail>(InstanceLifetime lifetime = InstanceLifetime.Scoped)
        where TAuditTrail : class, IAuditTrail
    {
        _messaging.UseAuditTrail<TAuditTrail>(lifetime);
        return this;
    }

    /// <summary>
    ///     Registers a pre-created <see cref="IAuditTrail" /> that receives audit records.
    /// </summary>
    /// <param name="auditTrail">The trail instance, shared by every mediation for the life of the process.</param>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     The name says the lifetime, because a pre-created instance can only be a singleton. A trail built here with
    ///     a database session captures that one session forever.
    /// </remarks>
    public AuditingBuilder UseTrailInstance(IAuditTrail auditTrail)
    {
        _messaging.UseAuditTrailInstance(auditTrail);
        return this;
    }

    /// <summary>
    ///     Registers the <see cref="IAuditActorResolver" /> that says who an audited action is attributed to.
    /// </summary>
    /// <typeparam name="TAuditActorResolver">The resolver implementation.</typeparam>
    /// <param name="lifetime">
    ///     The lifetime the resolver is resolved with. Defaults to <see cref="InstanceLifetime.Scoped" />, which is
    ///     what a resolver reading the authenticated principal of the request in flight needs.
    /// </param>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     Without one, every record is written with no actor, which the <c>litebus.audit.trail</c> probe reports as
    ///     degraded. A trail that cannot say who acted answers the second question a review asks and not the first.
    /// </remarks>
    public AuditingBuilder UseActorResolver<TAuditActorResolver>(
        InstanceLifetime lifetime = InstanceLifetime.Scoped)
        where TAuditActorResolver : class, IAuditActorResolver
    {
        _messaging.UseAuditActorResolver<TAuditActorResolver>(lifetime);
        return this;
    }

    /// <summary>
    ///     Registers a pre-created <see cref="IAuditActorResolver" />.
    /// </summary>
    /// <param name="auditActorResolver">The resolver instance, shared for the life of the process.</param>
    /// <returns>The current builder.</returns>
    public AuditingBuilder UseActorResolverInstance(IAuditActorResolver auditActorResolver)
    {
        _messaging.UseAuditActorResolverInstance(auditActorResolver);
        return this;
    }

    /// <summary>
    ///     Registers the <see cref="IAuditOutcomeMapper" /> used to classify how an audited action ended.
    /// </summary>
    /// <typeparam name="TAuditOutcomeMapper">The mapper implementation, constructed once at configuration time.</typeparam>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     Only an application that refuses by throwing needs one, so that its refusal exception is recorded as
    ///     <see cref="AuditOutcome.Denied" /> rather than <see cref="AuditOutcome.Failed" />. Refusing through a guard
    ///     or a validator needs no mapper.
    /// </remarks>
    public AuditingBuilder UseOutcomeMapper<TAuditOutcomeMapper>()
        where TAuditOutcomeMapper : IAuditOutcomeMapper, new()
    {
        _messaging.UseAuditOutcomeMapper<TAuditOutcomeMapper>();
        return this;
    }

    /// <summary>
    ///     Registers a pre-created <see cref="IAuditOutcomeMapper" />.
    /// </summary>
    /// <param name="auditOutcomeMapper">The mapper to register, shared for the life of the process.</param>
    /// <returns>The current builder.</returns>
    public AuditingBuilder UseOutcomeMapperInstance(IAuditOutcomeMapper auditOutcomeMapper)
    {
        _messaging.UseAuditOutcomeMapper(auditOutcomeMapper);
        return this;
    }

    /// <summary>
    ///     Audits command mediations.
    /// </summary>
    /// <returns>The current builder.</returns>
    public AuditingBuilder ForCommands()
    {
        _composition.Commands = true;
        return this;
    }

    /// <summary>
    ///     Audits query mediations.
    /// </summary>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     Reads are audited for the same reason writes are: who looked at what is the question a data-protection
    ///     review asks, and a trail of writes alone cannot answer it.
    /// </remarks>
    public AuditingBuilder ForQueries()
    {
        _composition.Queries = true;
        return this;
    }

    /// <summary>
    ///     Audits event mediations, writing one record per publish.
    /// </summary>
    /// <returns>The current builder.</returns>
    public AuditingBuilder ForEvents()
    {
        _composition.Events = true;
        return this;
    }

    /// <summary>
    ///     Audits every axis the application registered.
    /// </summary>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     An axis that is not registered is unaffected, so this is safe on a host that composes only commands. Only
    ///     messages that declare an audited position produce records either way, so the cost is per declaration rather
    ///     than per axis.
    /// </remarks>
    public AuditingBuilder ForAllAxes()
    {
        return ForCommands().ForQueries().ForEvents();
    }
}
