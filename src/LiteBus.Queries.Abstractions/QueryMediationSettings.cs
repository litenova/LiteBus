using System;
using System.Collections.Generic;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Queries.Abstractions;

/// <summary>
///     Represents the configuration settings that control query mediation behavior.
/// </summary>
/// <remarks>
///     QueryMediationSettings allows customizing how queries are processed in the pipeline,
///     including filtering which handlers participate in query processing.
///     These settings can be provided when sending queries through the IQueryMediator.
/// </remarks>
public sealed class QueryMediationSettings
{
    /// <summary>
    ///     Gets or initializes the query routing configuration that determines which handlers should execute.
    /// </summary>
    /// <value>
    ///     A <see cref="QueryRoutingSettings" /> instance containing routing configuration.
    /// </value>
    public QueryRoutingSettings Routing { get; init; } = new();

    /// <summary>
    ///     Gets the filters to be applied during query mediation.
    /// </summary>
    /// <remarks>
    ///     Filters determine which handlers participate in the query processing pipeline.
    ///     Prefer <see cref="Routing" /> for new code; this property remains for backward compatibility.
    /// </remarks>
    public QueryMediationFilters Filters { get; } = new();

    /// <summary>
    ///     Gets a key-value collection that can be used to pass contextual data through the mediation pipeline.
    /// </summary>
    /// <remarks>
    ///     This collection provides a mechanism for different components in the pipeline (such as pre-handlers,
    ///     post-handlers, or custom middleware) to share state or influence behavior without modifying the
    ///     command contract itself. For instance, a flag could be set to bypass a certain validation
    ///     step under specific, controlled conditions.
    /// </remarks>
    public IDictionary<string, object> Items { get; init; } = new Dictionary<string, object>();

    /// <summary>
    ///     Represents the filters to be applied during query mediation.
    /// </summary>
    /// <remarks>
    ///     Query mediation filters allow for selective inclusion of handlers in the query processing pipeline
    ///     based on their metadata such as tags.
    /// </remarks>
    public sealed class QueryMediationFilters
    {
        /// <summary>
        ///     Gets or sets the collection of tags used to filter query handlers (pre-handlers, main handlers, and post-handlers)
        ///     during mediation.
        /// </summary>
        /// <remarks>
        ///     When tags are specified, only handlers marked with at least one matching tag will participate in query processing.
        ///     If the collection is empty, all registered handlers will be considered.
        /// </remarks>
        public IEnumerable<string> Tags { get; set; } = new List<string>();

        /// <summary>
        ///     Gets or sets a predicate function used to filter query handlers by their descriptor.
        /// </summary>
        /// <remarks>
        ///     This predicate is evaluated for each potential handler descriptor before execution.
        ///     Use this for advanced filtering scenarios beyond tag-based filtering.
        ///     The predicate is applied after tag filtering.
        /// </remarks>
        public Func<IHandlerDescriptor, bool> HandlerPredicate { get; set; } = _ => true;
    }
}