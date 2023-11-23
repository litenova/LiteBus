using System;

namespace LiteBus.Events.Abstractions;

/// <summary>
/// Represents the settings used for event mediation.
/// </summary>
public sealed class EventMediationSettings
{
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
    public Func<Type, bool> FilterHandler { get; init; } = _ => true;
}