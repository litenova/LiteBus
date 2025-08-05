using System.Collections.Generic;

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
    ///     Gets the filters to be applied during query mediation.
    /// </summary>
    /// <remarks>
    ///     Filters determine which handlers participate in the query processing pipeline.
    /// </remarks>
    public QueryMediationFilters Filters { get; } = new();

    /// <summary>
    ///     Gets a key/value collection that can be used to share data within the scope of this execution.
    /// </summary>
    /// <remarks>
    ///     This collection allows handlers to share data with each other during the execution of a single
    ///     mediation operation.
    /// </remarks>
    public IDictionary<object, object?> Items { get; init; } = new Dictionary<object, object?>();

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
    }
}