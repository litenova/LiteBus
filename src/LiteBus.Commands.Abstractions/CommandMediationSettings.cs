using System.Collections.Generic;

namespace LiteBus.Commands.Abstractions;

/// <summary>
///     Represents the configuration settings that control command mediation behavior.
/// </summary>
/// <remarks>
///     CommandMediationSettings allows customizing how commands are processed in the pipeline,
///     including filtering which handlers participate in command processing.
///     These settings can be provided when sending commands through the ICommandMediator.
/// </remarks>
public sealed class CommandMediationSettings
{
    /// <summary>
    ///     Gets a key/value collection that can be used to share data within the scope of this execution.
    /// </summary>
    /// <remarks>
    ///     This collection allows handlers to share data with each other during the execution of a single
    ///     mediation operation.
    /// </remarks>
    public IDictionary<object, object?> Items { get; init; } = new Dictionary<object, object?>();

    /// <summary>
    ///     Gets the filters to be applied during command mediation.
    /// </summary>
    /// <remarks>
    ///     Filters determine which handlers participate in the command processing pipeline.
    /// </remarks>
    public CommandMediationFilters Filters { get; } = new();

    /// <summary>
    ///     Represents the filters to be applied during command mediation.
    /// </summary>
    /// <remarks>
    ///     Command mediation filters allow for selective inclusion of handlers in the command processing pipeline
    ///     based on their metadata such as tags.
    /// </remarks>
    public sealed class CommandMediationFilters
    {
        /// <summary>
        ///     Gets or sets the collection of tags used to filter command handlers (pre-handlers, main handlers, and
        ///     post-handlers) during mediation.
        /// </summary>
        /// <remarks>
        ///     When tags are specified, only handlers marked with at least one matching tag will participate in command
        ///     processing.
        ///     If the collection is empty, all registered handlers will be considered.
        /// </remarks>
        public IEnumerable<string> Tags { get; set; } = new List<string>();
    }
}