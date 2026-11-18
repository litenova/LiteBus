using System.Collections.Generic;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Queries.Abstractions;

/// <summary>
///     Represents the configuration settings that control query mediation behavior.
/// </summary>
/// <remarks>
///     QueryMediationSettings allows customizing how queries are processed in the pipeline,
///     including filtering which handlers participate in query processing.
///     These settings can be provided when sending queries through the IQueryMediator.
/// </remarks>
public sealed class QueryMediationSettings
{
    /// <summary>
    ///     Gets or initializes the query routing configuration that determines which handlers should execute.
    /// </summary>
    /// <value>
    ///     A <see cref="QueryRoutingSettings" /> instance containing routing configuration.
    /// </value>
    public QueryRoutingSettings Routing { get; init; } = new();

    /// <summary>
    ///     Gets a key-value collection that can be used to pass contextual data through the mediation pipeline.
    /// </summary>
    /// <remarks>
    ///     This collection provides a mechanism for different components in the pipeline (such as pre-handlers,
    ///     post-handlers, or custom middleware) to share state or influence behavior without modifying the
    ///     command contract itself. For instance, a flag could be set to bypass a certain validation
    ///     step under specific, controlled conditions.
    /// </remarks>
    public IDictionary<string, object> Items { get; init; } = new Dictionary<string, object>();

    /// <summary>
    ///     Gets the identity the mediation runs for: the calling principal and the tenant.
    /// </summary>
    /// <remarks>
    ///     Leave it <see langword="null" /> to inherit the identity of an enclosing mediation or to read it from the
    ///     registered <see cref="IExecutionIdentityProvider" />. Set it when the caller is known only here, such as a
    ///     job that runs on behalf of one tenant.
    /// </remarks>
    public ExecutionIdentity? Identity { get; init; }
}
