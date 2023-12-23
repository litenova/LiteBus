#nullable enable

using System;
using System.Collections.Generic;

namespace LiteBus.Events.Abstractions;

/// <summary>
/// Represents the settings used for event mediation.
/// </summary>
public sealed class EventMediationSettings
{
    /// <summary>
    /// Gets or sets a value indicating whether to throw an exception when no handler is found for an event.
    /// </summary>
    /// <remarks>
    /// When set to true, the mediator will throw an exception if it cannot find a suitable handler
    /// for a published event. The default behavior is false, which means the mediator will silently
    /// ignore such cases.
    /// </remarks>
    /// <value>
    /// True to throw an exception when no handler is found; false to ignore and proceed silently.
    /// </value>
    public bool ThrowIfNoHandlerFound { get; init; } = false;

    /// <summary>
    /// Gets the filters to be applied during event mediation.
    /// </summary>
    public EventMediationFilters Filters { get; } = new();

    /// <summary>
    /// Represents the filters to be applied during event mediation.
    /// </summary>
    public sealed class EventMediationFilters
    {
        /// <summary>
        /// Gets or sets the collection of tags used to filter event handlers (i.e., pre, main and post) during mediation.
        /// </summary>
        public IEnumerable<string> Tags { get; set; } = new List<string>();

        /// <summary>
        /// Gets or initializes the function used to filter event handlers.
        /// </summary>
        /// <remarks>
        /// This filter function is used to determine whether a given event handler
        /// should be invoked based on the event type. The default behavior is to 
        /// allow all event handlers (i.e., the function returns true for all event types).
        /// </remarks>
        /// <example>
        /// This example shows how to set a filter to only allow handlers for event types
        /// that have the 'LocalExecution' attribute:
        /// <code>
        /// var settings = new EventMediationSettings
        /// {
        ///     FilterHandler = type => Attribute.IsDefined(type, typeof(LocalExecutionAttribute))
        /// };
        /// </code>
        /// </example>
        /// <value>
        /// A function that takes a <see cref="Type"/> representing the event handler type
        /// and returns a boolean indicating whether the handler should be invoked.
        /// </value>
        public Func<Type, bool> HandlerPredicate { get; set; } = _ => true;
    }
}