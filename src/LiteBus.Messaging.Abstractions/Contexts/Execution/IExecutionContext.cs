using System.Collections.Generic;
using System.Threading;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Represents the execution context for a specific operation or task.
/// </summary>
/// <remarks>
///     The execution context provides access to information and services that are relevant to the current
///     execution, such as cancellation tokens, shared data, and tags for filtering handlers.
///     It also provides a mechanism for aborting the execution and setting a result.
///     The execution context is typically created at the beginning of a mediation operation and is
///     available throughout the entire mediation pipeline, including pre-handlers, main handlers,
///     post-handlers, and error handlers.
/// </remarks>
public interface IExecutionContext
{
    /// <summary>
    ///     Gets the cancellation token associated with the execution context.
    /// </summary>
    /// <remarks>
    ///     This token can be used to cancel the execution of the current operation.
    ///     Handlers should periodically check this token and abort their execution if cancellation is requested.
    /// </remarks>
    CancellationToken CancellationToken { get; }

    /// <summary>
    ///     Gets a key-value collection that can be used to pass contextual data through the mediation pipeline.
    /// </summary>
    /// <remarks>
    ///     This collection provides a mechanism for different components in the pipeline (such as pre-handlers,
    ///     post-handlers, or custom middleware) to share state or influence behavior without modifying the
    ///     command contract itself. For instance, a flag could be set to bypass a certain validation
    ///     step under specific, controlled conditions.
    /// </remarks>
    public IDictionary<string, object> Items { get; }

    /// <summary>
    ///     Gets the collection of specified tags used to filter message handlers (i.e., pre, main and post) during mediation.
    /// </summary>
    /// <remarks>
    ///     Tags are used to categorize handlers and allow for selective execution of handlers based on the
    ///     current execution context. Only handlers with matching tags will be executed during the mediation process.
    /// </remarks>
    IReadOnlyCollection<string> Tags { get; }

    /// <summary>
    ///     Gets or sets the result of the message mediation.
    /// </summary>
    /// <remarks>
    ///     This property can be set by handlers to provide a result for the mediation operation.
    ///     It is typically set by the main handler, and a post-handler may overwrite it to transform what the caller
    ///     receives. A short-circuiting pre-handler supplies its result through
    ///     <see cref="PipelineDirective.ShortCircuit" /> rather than through this property.
    /// </remarks>
    object? MessageResult { get; set; }

    /// <summary>
    ///     Gets a value indicating whether post-handlers are suppressed for this mediation.
    /// </summary>
    bool PostHandlersSuppressed { get; }

    /// <summary>
    ///     Suppresses the post-handlers that have not run yet.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Use this when the work turned out to be a no-op and the reactions to it should not fire. An idempotent
    ///         command that detects it already ran can return the existing result while suppressing the post-handler
    ///         that publishes its domain events.
    ///     </para>
    ///     <para>
    ///         Unlike short-circuiting, this does not stop the calling handler and does not change the outcome. The
    ///         mediation still reports <see cref="MessageOutcome.Succeeded" />, because the main handler ran. To stop
    ///         the pipeline before the work happens, implement
    ///         <see cref="IShortCircuitingPreHandler{TMessage}" /> instead.
    ///     </para>
    /// </remarks>
    void SuppressPostHandlers();
}
