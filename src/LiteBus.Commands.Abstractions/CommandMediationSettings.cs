#nullable enable

using System.Collections.Generic;

namespace LiteBus.Commands.Abstractions;

/// <summary>
/// Represents the settings used during command mediation.
/// </summary>
public sealed class CommandMediationSettings
{
    /// <summary>
    /// Gets the filters to be applied during command mediation.
    /// </summary>
    public CommandMediationFilters Filters { get; } = new();

    /// <summary>
    /// Represents the filters to be applied during command mediation.
    /// </summary>
    public sealed class CommandMediationFilters
    {
        /// <summary>
        /// Gets or sets the collection of tags used to filter command handlers (i.e., pre, main and post) during mediation.
        /// </summary>
        public IEnumerable<string> Tags { get; set; } = new List<string>();
    }
}