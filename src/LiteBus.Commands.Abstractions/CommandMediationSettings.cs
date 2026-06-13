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
    ///     Gets or initializes the command routing configuration that determines which handlers should execute.
    /// </summary>
    /// <value>
    ///     A <see cref="CommandRoutingSettings" /> instance containing routing configuration.
    /// </value>
    public CommandRoutingSettings Routing { get; init; } = new();

    /// <summary>
    ///     Gets a key-value collection that can be used to pass contextual data through the mediation pipeline.
    /// </summary>
    /// <remarks>
    ///     This collection provides a mechanism for different components in the pipeline (such as pre-handlers,
    ///     post-handlers, or custom middleware) to share state or influence behavior without modifying the
    ///     command contract itself. For instance, a flag could be set to bypass a certain validation
    ///     step under specific, controlled conditions.
    /// </remarks>
    public IDictionary<string, object> Items { get; } = new Dictionary<string, object>();
}
