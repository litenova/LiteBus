namespace LiteBus.Extensions.AspNetCore;

/// <summary>
///     Item keys the idempotency-key filter, settings bridge, and guard use to hand a key from one stage to the next.
/// </summary>
/// <remarks>
///     <para>
///         The filter writes the validated header value under <see cref="HttpContextItem" />, which keeps it available
///         for the duration of the endpoint invocation. The endpoint copies it into mediation settings under
///         <see cref="MediationItem" /> as a <see cref="LiteBus.Messaging.Abstractions.SuppliedIdempotencyKey" />, and
///         the guard reads that entry back before the idempotency shortcut asks for it. A tenant the filter resolved
///         travels the same two hops under <see cref="TenantHttpContextItem" /> and the trace tenant entry.
///     </para>
///     <para>
///         The two entries live in different dictionaries, so the keys never collide: one is an
///         <c>HttpContext.Items</c> entry scoped to the HTTP request, and the other is a mediation settings entry
///         scoped to one command mediation.
///     </para>
/// </remarks>
internal static class IdempotencyKeyItemKeys
{
    /// <summary>
    ///     The <c>HttpContext.Items</c> entry holding the validated header value for the endpoint invocation.
    /// </summary>
    internal const string HttpContextItem = "__LiteBus.IdempotencyKey.HttpContextItem";

    /// <summary>
    ///     The mediation settings entry holding the key the guard hands to the idempotency shortcut.
    /// </summary>
    internal const string MediationItem = "__LiteBus.IdempotencyKey.MediationItem";

    /// <summary>
    ///     The <c>HttpContext.Items</c> entry holding the resolved tenant for the endpoint invocation.
    /// </summary>
    internal const string TenantHttpContextItem = "__LiteBus.IdempotencyKey.TenantHttpContextItem";
}
