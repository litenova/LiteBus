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
}
