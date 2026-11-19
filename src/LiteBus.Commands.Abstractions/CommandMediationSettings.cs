using System.Collections.Generic;
using LiteBus.Messaging.Abstractions;

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

    /// <summary>
    ///     Gets the identity the mediation runs for: the calling principal and the tenant.
    /// </summary>
    /// <remarks>
    ///     Leave it <see langword="null" /> to inherit the identity of an enclosing mediation or to read it from the
    ///     registered <see cref="IExecutionIdentityProvider" />. Set it when the caller is known only here, such as a
    ///     job that runs on behalf of one tenant.
    /// </remarks>
    public ExecutionIdentity? Identity { get; init; }

    /// <summary>
    ///     Creates command mediation settings that run in one tenant.
    /// </summary>
    /// <param name="tenantId">The tenant the mediation runs in, or <see langword="null" /> when the work belongs to no tenant.</param>
    /// <returns>The settings with <see cref="Identity" /> set from <paramref name="tenantId" />; a blank value yields <see cref="ExecutionIdentity.Anonymous" />.</returns>
    public static CommandMediationSettings ForTenant(string? tenantId)
    {
        return new CommandMediationSettings { Identity = ExecutionIdentity.ForTenant(tenantId) };
    }
}
