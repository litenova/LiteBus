using System.Collections.Generic;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Events.Abstractions;

/// <summary>
///     Configures how events are mediated through the event pipeline.
/// </summary>
/// <remarks>
///     Event mediation controls the routing and execution of event handlers. Handlers can be organized
///     into priority groups using the <see cref="HandlerPriorityAttribute" />, where handlers with the same
///     priority value form a group. The mediation settings determine how these priority groups execute
///     relative to each other and how handlers within the same priority group execute concurrently.
/// </remarks>
public sealed class EventMediationSettings
{
    /// <summary>
    ///     Gets or sets whether to throw an exception when no handlers are found for an event.
    /// </summary>
    /// <value>
    ///     <c>true</c> to throw an exception when no handlers are found; otherwise, <c>false</c>.
    ///     The default is <c>false</c>.
    /// </value>
    public bool ThrowIfNoHandlerFound { get; init; } = false;

    /// <summary>
    ///     Gets the event mediation routing configuration that determines which handlers should execute.
    /// </summary>
    /// <value>
    ///     An <see cref="EventMediationRoutingSettings" /> instance containing routing configuration.
    /// </value>
    public EventMediationRoutingSettings Routing { get; init; } = new();

    /// <summary>
    ///     Gets the event mediation execution configuration that controls how handlers are executed.
    /// </summary>
    /// <value>
    ///     An <see cref="EventMediationExecutionSettings" /> instance containing execution configuration.
    /// </value>
    public EventMediationExecutionSettings Execution { get; init; } = new();

    /// <summary>
    ///     Gets a key-value collection that can be used to pass contextual data through the mediation pipeline.
    /// </summary>
    /// <remarks>
    ///     This collection provides a mechanism for different components in the pipeline (such as pre-handlers,
    ///     post-handlers, or custom middleware) to share state or influence behavior without modifying the
    ///     event contract itself.
    /// </remarks>
    public IDictionary<string, object> Items { get; init; } = new Dictionary<string, object>();
}