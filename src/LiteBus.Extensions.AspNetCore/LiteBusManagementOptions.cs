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
    ///     Gets or sets the authorization policy name applied to management endpoints.
    /// </summary>
    /// <value>
    ///     The policy name registered with ASP.NET Core authorization. When <see langword="null" />, endpoints are
    ///     mapped without authorization.
    /// </value>
    public string? AuthorizationPolicy { get; set; }
}
