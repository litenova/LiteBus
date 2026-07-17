namespace LiteBus.Extensions.AspNetCore;

/// <summary>
///     Options for mapping LiteBus operator management endpoints.
/// </summary>
public sealed class LiteBusManagementOptions
{
    /// <summary>
    ///     Gets or sets the route prefix for management endpoints.
    /// </summary>
    /// <value>The route prefix. The default is <c>litebus</c>.</value>
    public string RoutePrefix { get; set; } = "litebus";

    /// <summary>
    ///     Gets or sets a value indicating whether management endpoints allow anonymous callers.
    /// </summary>
    /// <value>
    ///     The default is <see langword="false" />. Set to <see langword="true" /> only for local
    ///     development or tightly controlled demos. Production hosts should keep the default and
    ///     register ASP.NET Core authentication plus <see cref="AuthorizationPolicy" />.
    /// </value>
    public bool AllowAnonymousManagement { get; set; }

    /// <summary>
    ///     Gets or sets the authorization policy name applied to management endpoints.
    /// </summary>
    /// <value>
    ///     The policy name registered with ASP.NET Core authorization. When <see langword="null" /> and
    ///     <see cref="AllowAnonymousManagement" /> is <see langword="false" />, endpoints require any
    ///     authenticated principal via <c>RequireAuthorization()</c>.
    /// </value>
    public string? AuthorizationPolicy { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether <c>GET /litebus/health</c> reports degraded when the manifest has
    ///     zero diagnostic probes.
    /// </summary>
    /// <value>
    ///     The default is <see langword="true" /> for production hosts. Local samples typically set
    ///     <see langword="false" /> so demos work without registering probes.
    /// </value>
    public bool FailHealthWhenNoProbes { get; set; } = true;

    /// <summary>
    ///     Gets or sets the default processor drain timeout used by HTTP drain endpoints.
    /// </summary>
    /// <value>The default drain timeout. The default is 30 seconds.</value>
    public TimeSpan DefaultDrainTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     Gets or sets the maximum number of rows returned by one management query page.
    /// </summary>
    /// <value>The default is 100.</value>
    public int MaxPageSize { get; set; } = 100;

    /// <summary>
    ///     Gets or sets the maximum number of message identifiers accepted by one bulk request.
    /// </summary>
    /// <value>The default is 1,000.</value>
    public int MaxBulkMessageIds { get; set; } = 1000;

    /// <summary>
    ///     Gets or sets the maximum timeout accepted by processor drain endpoints.
    /// </summary>
    /// <value>The default is five minutes.</value>
    public TimeSpan MaxDrainTimeout { get; set; } = TimeSpan.FromMinutes(5);
}
