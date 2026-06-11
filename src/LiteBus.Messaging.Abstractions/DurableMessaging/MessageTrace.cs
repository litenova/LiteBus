namespace LiteBus.Messaging.Abstractions.DurableMessaging;

/// <summary>
///     Describes distributed tracing metadata persisted with a durable message envelope.
/// </summary>
/// <remarks>
///     <para>
///         Each variant carries the fields required for its tracing shape. Absence of tracing is represented by
///         <see cref="None" />, not nullable properties on a shared record.
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
        ///     Initializes a new instance of the <see cref="None" /> class.
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
}