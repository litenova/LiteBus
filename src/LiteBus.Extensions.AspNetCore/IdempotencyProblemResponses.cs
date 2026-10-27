using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;

namespace LiteBus.Extensions.AspNetCore;

/// <summary>
///     Builds the problem responses the idempotency-key integration answers invalid requests and refusals with.
/// </summary>
/// <remarks>
///     The shape mirrors the management endpoint problems: a stable <c>type</c>, a short <c>title</c>, a safe
///     <c>detail</c> that never echoes the key, the request path as <c>instance</c>, and the request trace identifier
///     as <c>traceId</c>, so one client handler reads every LiteBus problem the same way.
/// </remarks>
internal static class IdempotencyProblemResponses
{
    /// <summary>
    ///     Creates a problem response with a stable type and the request trace identifier.
    /// </summary>
    /// <param name="httpContext">The current HTTP request context.</param>
    /// <param name="statusCode">The HTTP status code.</param>
    /// <param name="title">The stable problem title.</param>
    /// <param name="detail">The safe problem detail.</param>
    /// <param name="type">The stable problem type.</param>
    /// <returns>The problem response.</returns>
    internal static IResult Problem(HttpContext httpContext, int statusCode, string title, string detail, string type)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(detail);
        ArgumentNullException.ThrowIfNull(type);

        return Results.Problem(
            detail: detail,
            instance: httpContext.Request.Path,
            statusCode: statusCode,
            title: title,
            type: type,
            extensions: new Dictionary<string, object?>
            {
                ["traceId"] = httpContext.TraceIdentifier
            });
    }
}
