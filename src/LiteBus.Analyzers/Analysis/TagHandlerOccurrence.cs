using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace LiteBus.Analyzers.Analysis;

/// <summary>
///     Describes one handler declaration that carries handler tags.
/// </summary>
internal sealed class TagHandlerOccurrence
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="TagHandlerOccurrence" /> class.
    /// </summary>
    /// <param name="handlerName">The handler type name.</param>
    /// <param name="location">The diagnostic location for the declaration.</param>
    /// <param name="tags">The handler tags in attribute and argument order.</param>
    internal TagHandlerOccurrence(string handlerName, Location location, ImmutableArray<string> tags)
    {
        HandlerName = handlerName;
        Location = location;
        Tags = tags;
    }

    /// <summary>
    ///     Gets the handler type name.
    /// </summary>
    internal string HandlerName { get; }

    /// <summary>
    ///     Gets the diagnostic location for the declaration.
    /// </summary>
    internal Location Location { get; }

    /// <summary>
    ///     Gets the handler tags in attribute and argument order.
    /// </summary>
    internal ImmutableArray<string> Tags { get; }
}
