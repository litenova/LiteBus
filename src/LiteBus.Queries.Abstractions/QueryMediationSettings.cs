#nullable enable

using System.Collections.Generic;

namespace LiteBus.Queries.Abstractions;

/// <summary>
/// Represents the settings used during query mediation.
/// </summary>
public sealed class QueryMediationSettings
{
    /// <summary>
    /// Gets the filters to be applied during query mediation.
    /// </summary>
    public QueryMediationFilters Filters { get; } = new();

    /// <summary>
    /// Represents the filters to be applied during query mediation.
    /// </summary>
    public sealed class QueryMediationFilters
    {
        /// <summary>
        /// Gets or sets the collection of tags used to filter query handlers (i.e., pre, main and post) during mediation.
        /// </summary>
        public IEnumerable<string> Tags { get; set; } = new List<string>();
    }
}