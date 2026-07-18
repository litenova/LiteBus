namespace LiteBus.Messaging.Abstractions.DurableMessaging;

/// <summary>
///     Describes tenant isolation metadata persisted with a durable message envelope.
/// </summary>
/// <remarks>
///     <para>
///         Use <see cref="TenantScope.Unscoped" /> for single-tenant applications or when tenant context is not required.
///         Use <see cref="TenantScope.Isolated" /> when multi-tenant routing, filtering, or operational tooling needs a tenant id.
///     </para>
/// </remarks>
public abstract record TenantScope
{
    /// <summary>
    ///     Indicates that no tenant identifier is supplied for the message.
    /// </summary>
    public sealed record Unscoped : TenantScope
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="TenantScope.Unscoped" /> class.
        /// </summary>
        private Unscoped()
        {
        }

        /// <summary>
        ///     Gets the singleton instance used when tenant isolation is not requested.
        /// </summary>
        public static Unscoped Instance { get; } = new();
    }

    /// <summary>
    ///     Carries a tenant identifier used by multi-tenant applications and operational tooling.
    /// </summary>
    /// <param name="TenantId">The tenant identifier stored with the envelope.</param>
    public sealed record Isolated(string TenantId) : TenantScope;
}
