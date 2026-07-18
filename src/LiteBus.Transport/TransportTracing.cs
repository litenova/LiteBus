using System.Diagnostics;
using LiteBus.Runtime.Abstractions.Diagnostics;
using LiteBus.Transport.Abstractions;

namespace LiteBus.Transport;

/// <summary>
///     Creates OpenTelemetry activities for transport send and process operations.
/// </summary>
public static class TransportTracing
{
    /// <summary>
    ///     Gets the activity source used for transport send and process spans.
    /// </summary>
    public static readonly ActivitySource ActivitySource = new(LiteBusTransportTelemetry.ActivitySourceName);

    /// <summary>
    ///     Starts a producer span for one transport send operation.
    /// </summary>
    /// <param name="metadata">The broker and message metadata recorded on the activity.</param>
    /// <returns>A disposable activity that ends when disposed, or <see langword="null" /> when tracing is disabled.</returns>
    public static Activity? StartPublishActivity(TransportActivityMetadata metadata)
    {
        return StartActivity(LiteBusTransportTelemetry.PublishOperationName, ActivityKind.Producer, metadata, default);
    }

    /// <summary>
    ///     Starts a consumer span while one inbound transport delivery is processed.
    /// </summary>
    /// <param name="message">The received transport delivery.</param>
    /// <returns>A disposable activity that ends when disposed, or <see langword="null" /> when tracing is disabled.</returns>
    public static Activity? StartConsumeActivity(TransportMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        TryGetRemoteParentContext(message.Headers, out var remoteParentContext);

        return StartActivity(
            LiteBusTransportTelemetry.ConsumeOperationName,
            ActivityKind.Consumer,
            new TransportActivityMetadata
            {
                MessagingSystem = string.IsNullOrWhiteSpace(message.MessagingSystem)
                    ? TransportMessagingSystems.Other
                    : message.MessagingSystem,
                Destination = message.Destination,
                Route = message.Route,
                MessageId = message.MessageId,
                CorrelationId = message.CorrelationId,
                Redelivered = message.Redelivered
            },
            remoteParentContext);
    }

    /// <summary>
    ///     Records a failed transport operation on its activity.
    /// </summary>
    /// <param name="activity">The transport activity, when tracing is enabled.</param>
    /// <param name="exception">The exception that ended the operation.</param>
    public static void RecordException(Activity? activity, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        activity?.SetTag("error.type", exception.GetType().FullName ?? exception.GetType().Name);
        activity?.SetStatus(ActivityStatusCode.Error);
    }

    /// <summary>
    ///     Starts a transport activity with messaging semantic convention attributes.
    /// </summary>
    /// <param name="operationName">The system-specific operation name.</param>
    /// <param name="activityKind">The activity kind for the operation.</param>
    /// <param name="metadata">The broker and message metadata recorded on the activity.</param>
    /// <param name="remoteParentContext">The remote message creation context, when available.</param>
    /// <returns>The started activity, or <see langword="null" /> when no listener is subscribed.</returns>
    private static Activity? StartActivity(
        string operationName,
        ActivityKind activityKind,
        TransportActivityMetadata metadata,
        ActivityContext remoteParentContext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(metadata.MessagingSystem);

        if (!ActivitySource.HasListeners())
        {
            return null;
        }

        var tags = CreateTags(operationName, metadata);
        var activityName = CreateActivityName(operationName, metadata.Destination);
        var ambientParentContext = Activity.Current?.Context ?? default;
        var parentContext = ambientParentContext;
        ActivityLink[]? links = null;

        if (remoteParentContext.TraceId != default && remoteParentContext.SpanId != default)
        {
            if (ambientParentContext.TraceId != default && ambientParentContext.SpanId != default)
            {
                links = [new ActivityLink(remoteParentContext)];
            }
            else
            {
                parentContext = remoteParentContext;
            }
        }

        return ActivitySource.StartActivity(
            activityName,
            activityKind,
            parentContext,
            tags,
            links);
    }

    /// <summary>
    ///     Creates the OpenTelemetry and LiteBus attributes for one transport operation.
    /// </summary>
    /// <param name="operationName">The system-specific operation name.</param>
    /// <param name="metadata">The broker and message metadata recorded on the activity.</param>
    /// <returns>The activity tags supplied to the sampler at activity creation time.</returns>
    private static ActivityTagsCollection CreateTags(string operationName, TransportActivityMetadata metadata)
    {
        var tags = new ActivityTagsCollection
        {
            [LiteBusTransportTelemetry.MessagingSystemTagName] = metadata.MessagingSystem,
            [LiteBusTransportTelemetry.MessagingOperationNameTagName] = operationName,
            [LiteBusTransportTelemetry.MessagingOperationTypeTagName] = operationName
        };

        AddOptionalTag(tags, LiteBusTransportTelemetry.DestinationTagName, metadata.Destination);
        AddOptionalTag(tags, LiteBusTransportTelemetry.MessageIdTagName, metadata.MessageId);
        AddOptionalTag(tags, LiteBusTransportTelemetry.ConversationIdTagName, metadata.CorrelationId);

        if (!string.IsNullOrWhiteSpace(metadata.Route))
        {
            tags[LiteBusTransportTelemetry.RouteTagName] = metadata.Route;

            if (string.Equals(metadata.MessagingSystem, TransportMessagingSystems.Kafka, StringComparison.Ordinal))
            {
                tags[LiteBusTransportTelemetry.KafkaMessageKeyTagName] = metadata.Route;
            }
            else if (string.Equals(metadata.MessagingSystem, TransportMessagingSystems.RabbitMq, StringComparison.Ordinal))
            {
                tags[LiteBusTransportTelemetry.RabbitMqRoutingKeyTagName] = metadata.Route;
            }
        }

        if (metadata.Redelivered)
        {
            tags[LiteBusTransportTelemetry.RedeliveredTagName] = true;
        }

        return tags;
    }

    /// <summary>
    ///     Adds a non-empty string tag to an activity tag collection.
    /// </summary>
    /// <param name="tags">The activity tags receiving the value.</param>
    /// <param name="name">The tag name.</param>
    /// <param name="value">The optional tag value.</param>
    private static void AddOptionalTag(ActivityTagsCollection tags, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            tags[name] = value;
        }
    }

    /// <summary>
    ///     Creates a messaging span name from the operation and destination.
    /// </summary>
    /// <param name="operationName">The system-specific operation name.</param>
    /// <param name="destination">The optional destination name.</param>
    /// <returns>The low-cardinality activity name.</returns>
    private static string CreateActivityName(string operationName, string? destination)
    {
        return string.IsNullOrWhiteSpace(destination)
            ? operationName
            : string.Concat(operationName, " ", destination);
    }

    /// <summary>
    ///     Reads a W3C remote parent from the canonical LiteBus trace context header.
    /// </summary>
    /// <param name="headers">The received transport headers.</param>
    /// <param name="parentContext">The parsed remote parent when the header is valid.</param>
    /// <returns><see langword="true" /> when a valid W3C parent was parsed; otherwise <see langword="false" />.</returns>
    private static bool TryGetRemoteParentContext(
        IReadOnlyDictionary<string, object?> headers,
        out ActivityContext parentContext)
    {
        parentContext = default;
        var serializedContext = TransportHeaderValues.GetString(headers, TransportHeaders.TraceContext);

        if (string.IsNullOrWhiteSpace(serializedContext))
        {
            return false;
        }

        return W3CTraceContextParser.TryParse(serializedContext, out parentContext);
    }
}
