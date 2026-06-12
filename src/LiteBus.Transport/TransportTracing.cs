using System.Diagnostics;
using LiteBus.Transport.Abstractions;

namespace LiteBus.Transport;

/// <summary>
///     Creates OpenTelemetry activities for transport publish and consume operations.
/// </summary>
public static class TransportTracing
{
    /// <summary>
    ///     Gets the activity source used for transport publish and consume spans.
    /// </summary>
    public static readonly ActivitySource ActivitySource = new(LiteBusTransportTelemetry.ActivitySourceName);

    /// <summary>
    ///     Starts a publish span for one transport publication.
    /// </summary>
    /// <param name="destination">The primary destination address.</param>
    /// <param name="route">The route within the destination, when available.</param>
    /// <param name="messageId">The transport message identifier, when available.</param>
    /// <returns>A disposable scope that ends the activity when disposed, or <see langword="null" /> when tracing is disabled.</returns>
    public static Activity? StartPublishActivity(string? destination, string? route, string? messageId)
    {
        var activity = ActivitySource.StartActivity(
            LiteBusTransportTelemetry.PublishActivityName,
            ActivityKind.Producer,
            default(ActivityContext));

        if (activity is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(destination))
        {
            activity.SetTag("messaging.destination.name", destination);
        }

        if (!string.IsNullOrWhiteSpace(route))
        {
            activity.SetTag("messaging.kafka.message_key", route);
        }

        if (!string.IsNullOrWhiteSpace(messageId))
        {
            activity.SetTag("messaging.message.id", messageId);
        }

        return activity;
    }

    /// <summary>
    ///     Starts a consume span for one inbound transport delivery.
    /// </summary>
    /// <param name="message">The received transport delivery.</param>
    /// <returns>A disposable scope that ends the activity when disposed, or <see langword="null" /> when tracing is disabled.</returns>
    public static Activity? StartConsumeActivity(TransportMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var activity = ActivitySource.StartActivity(
            LiteBusTransportTelemetry.ConsumeActivityName,
            ActivityKind.Consumer,
            default(ActivityContext));

        if (activity is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(message.Destination))
        {
            activity.SetTag("messaging.destination.name", message.Destination);
        }

        if (!string.IsNullOrWhiteSpace(message.Route))
        {
            activity.SetTag("messaging.kafka.message_key", message.Route);
        }

        if (!string.IsNullOrWhiteSpace(message.MessageId))
        {
            activity.SetTag("messaging.message.id", message.MessageId);
        }

        if (!string.IsNullOrWhiteSpace(message.CorrelationId))
        {
            activity.SetTag("messaging.correlation_id", message.CorrelationId);
        }

        activity.SetTag("messaging.operation", "receive");
        return activity;
    }
}