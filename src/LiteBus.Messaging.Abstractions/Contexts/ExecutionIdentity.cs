using System.Security.Claims;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Describes who a mediation runs for: the calling principal and the tenant the work belongs to.
/// </summary>
/// <remarks>
///     <para>
///         The mediator resolves one identity per mediation and exposes it as <see cref="IExecutionContext.Identity" />,
///         so guards, handlers, audit actor resolution and reactions read the caller from the pipeline instead of from a
///         property every message carries.
///     </para>
///     <para>
///         Resolution order, first match wins: the identity passed on the mediation settings, the identity of the
///         enclosing mediation when mediations nest, the identity a durable dispatcher restores from the envelope
///         (tenant only; a principal is never persisted), the registered <see cref="IExecutionIdentityProvider" />,
///         and finally <see cref="Anonymous" />.
///     </para>
/// </remarks>
public sealed record ExecutionIdentity
{
    /// <summary>
    ///     Gets the identity with no principal and no tenant.
    /// </summary>
    public static ExecutionIdentity Anonymous { get; } = new();

    /// <summary>
    ///     Gets the identity for work the library starts itself, with no principal and no tenant.
    /// </summary>
    /// <remarks>
    ///     Unlike <see cref="Anonymous" />, which means no caller is known, <see cref="System" /> declares that the
    ///     mediation runs as the system, such as an outbox processor delivery or a recurring schedule firing.
    /// </remarks>
    public static ExecutionIdentity System { get; } = new() { IsSystem = true };

    /// <summary>
    ///     Gets the principal the mediation runs for, or <see langword="null" /> when no caller is known, such as in a
    ///     background reaction or a recurring job.
    /// </summary>
    public ClaimsPrincipal? Principal { get; init; }

    /// <summary>
    ///     Gets the tenant the mediation runs in, or <see langword="null" /> when the work belongs to no tenant.
    /// </summary>
    public string? TenantId { get; init; }

    /// <summary>
    ///     Gets a value indicating whether a tenant is known for the mediation.
    /// </summary>
    public bool HasTenant => !string.IsNullOrWhiteSpace(TenantId);

    /// <summary>
    ///     Gets a value indicating whether this identity declares system-started work.
    /// </summary>
    public bool IsSystem { get; init; }

    /// <summary>
    ///     Creates an identity for a principal and an optional tenant.
    /// </summary>
    /// <param name="principal">The calling principal, or <see langword="null" /> when no caller is known.</param>
    /// <param name="tenantId">The tenant the work belongs to, or <see langword="null" /> for none.</param>
    /// <returns>The identity.</returns>
    public static ExecutionIdentity For(ClaimsPrincipal? principal, string? tenantId = null)
    {
        return new ExecutionIdentity
        {
            Principal = principal,
            TenantId = string.IsNullOrWhiteSpace(tenantId) ? null : tenantId
        };
    }

    /// <summary>
    ///     Creates an identity that carries only a tenant, as a durable dispatcher restores it from an envelope.
    /// </summary>
    /// <param name="tenantId">The tenant the work belongs to, or <see langword="null" /> for none.</param>
    /// <returns>The identity, or <see cref="Anonymous" /> when <paramref name="tenantId" /> is blank.</returns>
    public static ExecutionIdentity ForTenant(string? tenantId)
    {
        return string.IsNullOrWhiteSpace(tenantId) ? Anonymous : new ExecutionIdentity { TenantId = tenantId };
    }
}
