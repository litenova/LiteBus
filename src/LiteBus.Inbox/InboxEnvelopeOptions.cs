using LiteBus.Messaging.Abstractions.DurableMessaging;

namespace LiteBus.Inbox.Abstractions;

/// <summary>
///     Configures inbox envelope creation at accept time.
/// </summary>
/// <remarks>
///     The inbox module registers this instance only when <see cref="InboxModuleBuilder.UseAmbientTrace" /> was
///     called. The envelope factory treats a missing instance as the default: accepts keep the trace their item
///     carries, and untraced items stay untraced.
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
}
