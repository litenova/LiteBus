using System;
using Microsoft.AspNetCore.Http;

namespace LiteBus.Extensions.AspNetCore;

/// <summary>
///     Options for the endpoint filter registered by <see cref="IdempotencyKeyEndpointExtensions" />.
/// </summary>
public sealed class IdempotencyKeyFilterOptions
{
    /// <summary>
    ///     Gets or sets the request header the filter reads the idempotency key from.
    /// </summary>
    /// <value>The header name. The default is <c>Idempotency-Key</c>.</value>
    public string HeaderName { get; set; } = "Idempotency-Key";

    /// <summary>
    ///     Gets or sets a value indicating whether a request without the header is rejected.
    /// </summary>
    /// <value>
    ///     The default is <see langword="true" />: a missing header short-circuits the endpoint with a
    ///     <c>400 Bad Request</c> problem response. Set to <see langword="false" /> when the key is optional and the
    ///     endpoint proceeds without one.
    /// </value>
    public bool RequireHeader { get; set; } = true;

    /// <summary>
    ///     Gets or sets the maximum accepted key length in characters.
    /// </summary>
    /// <value>
    ///     The default is 255. A longer key short-circuits the endpoint with a <c>400 Bad Request</c> problem
    ///     response.
    /// </value>
    public int MaxKeyLength { get; set; } = 255;

    /// <summary>
    ///     Gets or sets the resolver that supplies the tenant the idempotency key is scoped to.
    /// </summary>
    /// <value>
    ///     A function reading the tenant from the current request, or <see langword="null" /> (the default) when keys
    ///     are not tenant-scoped. A blank or missing tenant resolves to no tenant scope, so single-tenant endpoints
    ///     leave this unset.
    /// </value>
    /// <remarks>
    ///     A multi-tenant endpoint should set this. Without a tenant scope, two tenants sending one key string for
    ///     one command share a store row, and one tenant's repeat is answered with the other's stored result whenever
    ///     the fingerprints match or either side carries none. The resolved tenant travels with the key into the
    ///     mediation and prefixes the stored key.
    /// </remarks>
    /// <example>
    ///     <code><![CDATA[
    /// group.WithIdempotencyKey(options =>
    /// {
    ///     options.TenantResolver = context => context.User.FindFirst("tenant_id")?.Value;
    /// });
    /// ]]></code>
    /// </example>
    public Func<HttpContext, string?>? TenantResolver { get; set; }
}
