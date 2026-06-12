using System;
using System.Collections.Generic;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Commands.Abstractions;

/// <summary>
///     Configures command routing that determines which handlers participate in mediation.
/// </summary>
public sealed class CommandRoutingSettings
{
    /// <summary>
    ///     Gets or initializes the collection of tags used to filter handlers during command mediation.
    /// </summary>
    /// <value>
    ///     When empty, all registered handlers are considered. Otherwise only handlers with at least one matching tag run.
    /// </value>
    public IEnumerable<string> Tags { get; init; } = [];

    /// <summary>
    ///     Gets or initializes a predicate that filters handlers after tag filtering.
    /// </summary>
    /// <value>
    ///     The default accepts every handler descriptor.
    /// </value>
    public Func<IHandlerDescriptor, bool> HandlerPredicate { get; init; } = _ => true;
}
