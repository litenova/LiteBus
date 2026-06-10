namespace LiteBus.Inbox.Ingress;

/// <summary>
///     Public OpenTelemetry instrument names for transport inbox ingress.
/// </summary>
public static class LiteBusInboxIngressTelemetry
{
    /// <summary>
    ///     Gets the meter name used for transport inbox ingress metrics.
    /// </summary>
    public const string MeterName = "LiteBus.Inbox";

    /// <summary>
    ///     Gets the instrument name incremented when broker acknowledgement fails after a successful inbox accept.
    /// </summary>
    public const string AckFailedAfterAcceptInstrumentName = "ingress.ack_failed_after_accept";
}
