using LiteBus.Messaging.Abstractions.DurableMessaging;

namespace LiteBus.Inbox.Abstractions;

/// <summary>
///     Configures inbox envelope creation at accept time.
/// </summary>
/// <remarks>
///     The inbox module registers this instance only when <see cref="InboxModuleBuilder.UseAmbientTrace" /> or
///     <see cref="InboxModuleBuilder.UseAmbientTenant" /> was called. The envelope factory treats a missing instance as
///     the default: accepts keep the trace and tenant their item carries, untraced items stay untraced, and unscoped
///     items stay unscoped.
/// </remarks>
public sealed record InboxEnvelopeOptions
{
    /// <summary>
    ///     Gets a value indicating whether untraced accepts inherit trace metadata from the ambient execution context.
    /// </summary>
    /// <value>
    ///     <see langword="true" /> when the envelope factory replaces <see cref="MessageTrace.None" /> with
    ///     <see cref="MessageTrace.FromAmbient" />; otherwise, <see langword="false" />. The default is
    ///     <see langword="false" />.
    /// </value>
    public bool UseAmbientTrace { get; init; }

    /// <summary>
    ///     Gets a value indicating whether unscoped accepts inherit tenant metadata from the ambient execution identity.
    /// </summary>
    /// <value>
    ///     <see langword="true" /> when the envelope factory replaces <see cref="TenantScope.Unscoped" /> with the
    ///     ambient tenant; otherwise, <see langword="false" />. The default is <see langword="false" />.
    /// </value>
    public bool UseAmbientTenant { get; init; }
}
