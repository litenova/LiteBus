using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Services and ambient metadata available while a domain event translator runs.
/// </summary>
/// <remarks>
///     <para>
///         Translation runs inside the staging completion handler, before the unit of work commits and inside the
///         transaction's scope. A translator resolves scoped services through <see cref="ServiceProvider" />, which is
///         the mediation's dispatch-scope provider, so a translator that loads data observes the same uncommitted
///         writes the handlers produced.
///     </para>
///     <para>
///         The trace and tenant values are captured from the ambient execution context once per staging pass. They are
///         <see langword="null" /> when the mediation carries none, such as an application-started mediation without
///         trace metadata.
///     </para>
/// </remarks>
public sealed class DomainEventTranslationContext
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="DomainEventTranslationContext" /> class.
    /// </summary>
    /// <param name="mediatedMessageType">The type of the message being mediated when the domain event was staged.</param>
    /// <param name="serviceProvider">The mediation's dispatch-scope provider used to resolve scoped services.</param>
    /// <param name="correlationId">The ambient correlation identifier, or <see langword="null" /> when the mediation carries none.</param>
    /// <param name="causationId">The ambient causation identifier, or <see langword="null" /> when the mediation carries none.</param>
    /// <param name="tenantId">The tenant the mediation runs in, or <see langword="null" /> when the work belongs to no tenant.</param>
    public DomainEventTranslationContext(
        Type mediatedMessageType,
        IServiceProvider serviceProvider,
        string? correlationId = null,
        string? causationId = null,
        string? tenantId = null)
    {
        ArgumentNullException.ThrowIfNull(mediatedMessageType);
        ArgumentNullException.ThrowIfNull(serviceProvider);

        MediatedMessageType = mediatedMessageType;
        ServiceProvider = serviceProvider;
        CorrelationId = correlationId;
        CausationId = causationId;
        TenantId = tenantId;
    }

    /// <summary>
    ///     Gets the type of the message being mediated when the domain event was staged.
    /// </summary>
    public Type MediatedMessageType { get; }

    /// <summary>
    ///     Gets the mediation's dispatch-scope provider used to resolve scoped services.
    /// </summary>
    public IServiceProvider ServiceProvider { get; }

    /// <summary>
    ///     Gets the ambient correlation identifier, or <see langword="null" /> when the mediation carries none.
    /// </summary>
    public string? CorrelationId { get; }

    /// <summary>
    ///     Gets the ambient causation identifier, or <see langword="null" /> when the mediation carries none.
    /// </summary>
    public string? CausationId { get; }

    /// <summary>
    ///     Gets the tenant the mediation runs in, or <see langword="null" /> when the work belongs to no tenant.
    /// </summary>
    public string? TenantId { get; }
}
