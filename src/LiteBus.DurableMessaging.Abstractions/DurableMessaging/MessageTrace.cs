using System;
using System.Collections.Generic;

namespace LiteBus.Messaging.Abstractions.DurableMessaging;

/// <summary>
///     Describes distributed tracing metadata persisted with a durable message envelope.
/// </summary>
/// <remarks>
///     <para>
///         Each variant carries the fields required for its tracing shape. Absence of tracing is represented by
///         <see cref="MessageTrace.None" />, not nullable properties on a shared record.
///     </para>
/// </remarks>
public abstract record MessageTrace
{
    /// <summary>
    ///     Indicates that no trace metadata is supplied for the message.
    /// </summary>
    public sealed record None : MessageTrace
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="MessageTrace.None" /> class.
        /// </summary>
        private None()
        {
        }

        /// <summary>
        ///     Gets the singleton instance used when tracing metadata is not requested.
        /// </summary>
        public static None Instance { get; } = new();
    }

    /// <summary>
    ///     Carries a correlation identifier used to group logs, traces, and stored messages for one workflow.
    /// </summary>
    /// <param name="CorrelationId">The correlation identifier for the workflow.</param>
    public sealed record Correlated(string CorrelationId) : MessageTrace;

    /// <summary>
    ///     Carries correlation and causation identifiers that describe one step within a workflow.
    /// </summary>
    /// <param name="CorrelationId">The correlation identifier for the workflow.</param>
    /// <param name="CausationId">The identifier of the message or request that caused this message.</param>
    public sealed record Workflow(string CorrelationId, string CausationId) : MessageTrace;

    /// <summary>
    ///     Carries correlation, causation, and distributed trace context persisted as JSON text.
    /// </summary>
    /// <param name="CorrelationId">The correlation identifier for the workflow.</param>
    /// <param name="CausationId">The identifier of the message or request that caused this message.</param>
    /// <param name="TraceContext">The distributed trace context stored as JSON text.</param>
    public sealed record Distributed(string CorrelationId, string CausationId, string TraceContext) : MessageTrace;

    /// <summary>
    ///     The execution-context item key under which inbox dispatchers store the durable message identifier.
    /// </summary>
    /// <remarks>
    ///     This literal mirrors <c>InboxExecutionContextKeys.MessageId</c> in <c>LiteBus.Inbox.Abstractions</c>, which
    ///     this package cannot reference without a dependency cycle. The inbox ambient tests write the key through the
    ///     constant and assert this factory reads it, so a rename on either side fails loudly instead of silently
    ///     dropping the causation link.
    /// </remarks>
    private const string InboxMessageIdKey = "__LiteBus.Inbox.MessageId";

    /// <summary>
    ///     Builds trace metadata from the ambient execution context, when one exists.
    /// </summary>
    /// <returns>
    ///     The trace propagated from ambient state, or <see cref="None" /> when there is no ambient context or it
    ///     carries nothing usable.
    /// </returns>
    /// <remarks>
    ///     <para>
    ///         Reads back exactly what <see cref="MessageProcessorDiagnostics.ApplyTraceMetadata" /> writes: the ambient
    ///         correlation id, causation id, and distributed trace context. The incoming message id, stored by inbox
    ///         dispatchers when the mediation replays a stored envelope, becomes the causation id, and doubles as the
    ///         correlation id when the ambient correlation id is absent.
    ///     </para>
    ///     <para>
    ///         The variant follows the same precedence as envelope reconstruction: <see cref="Distributed" /> when a
    ///         trace context is present alongside correlation and causation, because it preserves the most; otherwise
    ///         <see cref="Workflow" /> when correlation and causation are both known; otherwise
    ///         <see cref="Correlated" /> when only the correlation id is known; otherwise <see cref="None" />. When no
    ///         incoming message id is stored, which is the case for outbox replay, the ambient causation id is
    ///         preserved so the causal chain is not broken.
    ///     </para>
    ///     <para>
    ///         Nothing reads ambient state unless the caller calls this factory: it is the explicit, opt-in half of
    ///         trace propagation, and writers keep storing <see cref="None" /> for callers that never call it.
    ///     </para>
    /// </remarks>
    public static MessageTrace FromAmbient()
    {
        var context = AmbientExecutionContext.GetCurrentOrDefault();

        if (context is null)
        {
            return None.Instance;
        }

        var incomingMessageId = ReadIncomingMessageId(context.Items);
        var correlationId = ReadTraceItem(context.Items, MessageTraceContextKeys.CorrelationId) ?? incomingMessageId;
        var causationId = incomingMessageId ?? ReadTraceItem(context.Items, MessageTraceContextKeys.CausationId);
        var traceContext = ReadTraceItem(context.Items, MessageTraceContextKeys.TraceContext);

        if (string.IsNullOrWhiteSpace(correlationId))
        {
            return None.Instance;
        }

        if (string.IsNullOrWhiteSpace(causationId))
        {
            return new Correlated(correlationId);
        }

        if (!string.IsNullOrWhiteSpace(traceContext))
        {
            return new Distributed(correlationId, causationId, traceContext);
        }

        return new Workflow(correlationId, causationId);
    }

    /// <summary>
    ///     Reads one trace value written by <see cref="MessageProcessorDiagnostics.ApplyTraceMetadata" />.
    /// </summary>
    /// <param name="items">The ambient execution-context items to read.</param>
    /// <param name="key">The trace item key to read.</param>
    /// <returns>The stored value, or <see langword="null" /> when absent or blank.</returns>
    private static string? ReadTraceItem(IDictionary<string, object> items, string key)
    {
        if (!items.TryGetValue(key, out var value))
        {
            return null;
        }

        var text = value as string;

        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    /// <summary>
    ///     Reads the incoming message identifier stored by inbox dispatchers.
    /// </summary>
    /// <param name="items">The ambient execution-context items to read.</param>
    /// <returns>
    ///     The incoming message identifier, or <see langword="null" /> when the mediation does not replay a stored
    ///     inbox envelope.
    /// </returns>
    private static string? ReadIncomingMessageId(IDictionary<string, object> items)
    {
        if (!items.TryGetValue(InboxMessageIdKey, out var value))
        {
            return null;
        }

        return value switch
        {
            Guid messageId => $"{messageId}",
            string text when !string.IsNullOrWhiteSpace(text) => text,
            _ => null
        };
    }
}
