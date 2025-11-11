using System.Collections.Generic;

namespace LiteBus.Events.Abstractions;

/// <summary>
///     Configures event mediation routing to determine which handlers should execute for an event.
/// </summary>
/// <remarks>
///     Routing settings provide filtering mechanisms to control which handlers participate in event processing.
///     This includes tag-based filtering and custom predicate filtering. Handlers that don't match the routing
///     criteria are excluded from execution entirely.
/// </remarks>
public sealed class EventMediationRoutingSettings
{
    /// <summary>
    ///     Gets or sets the collection of tags used to filter event handlers.
    /// </summary>
    /// <value>
    ///     A collection of string tags. Only handlers with matching tags or no tags will execute.
    ///     The default is an empty collection.
    /// </value>
    /// <remarks>
    ///     Handlers without any tags will always execute regardless of the tags specified here.
    ///     When multiple tags are specified, handlers matching any of the tags will execute (OR logic).
    /// </remarks>
    public IEnumerable<string> Tags { get; set; } = new List<string>();

    /// <summary>
    ///     Gets or sets a predicate function used to filter event handlers by their descriptor.
    /// </summary>
    /// <value>
    ///     An <see cref="EventHandlerFilterPredicate" /> that determines which handlers should execute.
    ///     The default predicate returns <c>true</c> for all handler descriptors.
    /// </value>
    /// <remarks>
    ///     This predicate is evaluated for each potential handler descriptor before execution.
    ///     Use this for advanced filtering scenarios beyond tag-based filtering.
    ///     The predicate is applied after tag filtering.
    /// </remarks>
    /// <seealso cref="EventHandlerFilterPredicate" />
    public EventHandlerFilterPredicate HandlerPredicate { get; init; } = _ => true;
}