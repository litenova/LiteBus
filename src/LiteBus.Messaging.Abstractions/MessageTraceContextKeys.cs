namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Defines execution-context item keys for trace metadata propagated by inbox and outbox processors.
/// </summary>
public static class MessageTraceContextKeys
{
    /// <summary>
    ///     Correlation identifier copied from a stored envelope into mediation settings.
    /// </summary>
    public const string CorrelationId = "__LiteBus.Trace.CorrelationId";

    /// <summary>
    ///     Causation identifier copied from a stored envelope into mediation settings.
    /// </summary>
    public const string CausationId = "__LiteBus.Trace.CausationId";

    /// <summary>
    ///     Tenant identifier copied from a stored envelope into mediation settings.
    /// </summary>
    public const string TenantId = "__LiteBus.Trace.TenantId";
}
