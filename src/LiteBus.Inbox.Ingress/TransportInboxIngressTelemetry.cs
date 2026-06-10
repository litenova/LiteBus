using System.Diagnostics.Metrics;

namespace LiteBus.Inbox.Ingress;

/// <summary>
///     OpenTelemetry metric instrumentation for transport inbox ingress.
/// </summary>
internal static class TransportInboxIngressTelemetry
{
    /// <summary>
    ///     Gets the meter used for ingress counters.
    /// </summary>
    private static readonly Meter Meter = new(LiteBusInboxIngressTelemetry.MeterName);

    /// <summary>
    ///     Gets the counter incremented when broker acknowledgement fails after a successful inbox accept.
    /// </summary>
    private static readonly Counter<long> AckFailedAfterAcceptCounter =
        Meter.CreateCounter<long>(LiteBusInboxIngressTelemetry.AckFailedAfterAcceptInstrumentName);

    /// <summary>
    ///     Records that broker acknowledgement failed after the inbox store accepted the delivery.
    /// </summary>
    public static void RecordAckFailedAfterAccept()
    {
        AckFailedAfterAcceptCounter.Add(1);
    }
}
