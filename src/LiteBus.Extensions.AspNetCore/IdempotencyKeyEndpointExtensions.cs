using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace LiteBus.Extensions.AspNetCore;

/// <summary>
///     Registers the endpoint filter that reads the idempotency-key request header.
/// </summary>
public static class IdempotencyKeyEndpointExtensions
{
    /// <summary>
    ///     Reads the idempotency-key request header into <c>HttpContext.Items</c> for endpoints that send idempotent
    ///     commands.
    /// </summary>
    /// <typeparam name="TBuilder">The endpoint convention builder type.</typeparam>
    /// <param name="builder">The route or group builder the filter is added to.</param>
    /// <param name="configure">Adjusts the filter options.</param>
    /// <returns>The builder for chaining.</returns>
    /// <remarks>
    ///     <para>
    ///         The filter runs before the endpoint. It reads the configured header, answers a missing required header
    ///         and an over-long key with a <c>400 Bad Request</c> problem response without running the endpoint, and
    ///         stores the validated key in <c>HttpContext.Items</c> for the duration of the invocation. The endpoint
    ///         carries it into the mediation with <see cref="CommandMediationSettingsExtensions" />, and
    ///         <see cref="IdempotencyKeyGuard" /> hands it to the idempotency shortcut as a
    ///         <see cref="LiteBus.Messaging.Abstractions.SuppliedIdempotencyKey" />:
    ///     </para>
    ///     <code><![CDATA[
    /// endpoints.MapPost("/orders", async (HttpContext context, ICommandMediator mediator, PlaceOrder command, CancellationToken cancellationToken) =>
    /// {
    ///     var settings = new CommandMediationSettings().WithIdempotencyKey(context);
    ///     var result = await mediator.TrySendAsync(command, settings, cancellationToken).ConfigureAwait(false);
    ///     return result.ToHttpResult(context, () => Results.Ok());
    /// }).WithIdempotencyKey();
    /// ]]></code>
    ///     <para>
    ///         The command must declare
    ///         <see cref="LiteBus.Messaging.Abstractions.IdempotencyDeclaration.KeyedByContext" />, the application must
    ///         register <see cref="IdempotencyKeyGuard" /> on the command module, and the module must enable idempotency
    ///         with an <c>IIdempotencyStore</c>. Without the guard the key never reaches the execution context, and the
    ///         shortcut reports the missing key as a configuration error.
    ///     </para>
    ///     <para>
    ///         A blank header counts as missing. When several values arrive under one header name, the first value wins
    ///         and the rest are ignored.
    ///     </para>
    ///     <para>
    ///         A multi-tenant endpoint sets <see cref="IdempotencyKeyFilterOptions.TenantResolver" /> so the stored key
    ///         is prefixed by the calling tenant. Without it, two tenants sending one key string for one command share
    ///         a store row. The resolved tenant travels with the key into the mediation through
    ///         <see cref="CommandMediationSettingsExtensions" />.
    ///     </para>
    /// </remarks>
    public static TBuilder WithIdempotencyKey<TBuilder>(
        this TBuilder builder,
        Action<IdempotencyKeyFilterOptions>? configure = null)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = new IdempotencyKeyFilterOptions();
        configure?.Invoke(options);
        ValidateOptions(options);

        builder.AddEndpointFilter((EndpointFilterInvocationContext context, EndpointFilterDelegate next) =>
        {
            var httpContext = context.HttpContext;
            var key = ReadHeaderKey(httpContext, options);

            if (key is null)
            {
                return options.RequireHeader
                    ? ValueTask.FromResult<object?>(CreateMissingKeyProblem(httpContext, options))
                    : next(context);
            }

            if (key.Length > options.MaxKeyLength)
            {
                return ValueTask.FromResult<object?>(CreateKeyTooLongProblem(httpContext, options));
            }

            httpContext.Items[IdempotencyKeyItemKeys.HttpContextItem] = key;

            var tenant = options.TenantResolver?.Invoke(httpContext);

            if (!string.IsNullOrWhiteSpace(tenant))
            {
                httpContext.Items[IdempotencyKeyItemKeys.TenantHttpContextItem] = tenant;
            }

            return next(context);
        });

        return builder;
    }

    /// <summary>
    ///     Validates the filter options once at registration, so a misconfigured endpoint fails at startup rather than
    ///     on its first request.
    /// </summary>
    /// <param name="options">The configured filter options.</param>
    private static void ValidateOptions(IdempotencyKeyFilterOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(options.HeaderName);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxKeyLength);
    }

    /// <summary>
    ///     Reads the configured header value, or reports that no usable key arrived.
    /// </summary>
    /// <param name="httpContext">The current HTTP request context.</param>
    /// <param name="options">The filter options naming the header.</param>
    /// <returns>The trimmed key, or <see langword="null" /> when the header is absent or blank.</returns>
    private static string? ReadHeaderKey(HttpContext httpContext, IdempotencyKeyFilterOptions options)
    {
        var values = httpContext.Request.Headers[options.HeaderName];

        if (values.Count == 0)
        {
            return null;
        }

        var key = values[0]?.Trim();

        return string.IsNullOrEmpty(key) ? null : key;
    }

    /// <summary>
    ///     Creates the problem response used when a required header is missing or blank.
    /// </summary>
    /// <param name="httpContext">The current HTTP request context.</param>
    /// <param name="options">The filter options naming the header.</param>
    /// <returns>The missing-key problem response.</returns>
    private static IResult CreateMissingKeyProblem(HttpContext httpContext, IdempotencyKeyFilterOptions options)
    {
        return IdempotencyProblemResponses.Problem(
            httpContext,
            StatusCodes.Status400BadRequest,
            "Missing idempotency key",
            $"The request must carry an '{options.HeaderName}' header.",
            "urn:litebus:idempotency:missing-key");
    }

    /// <summary>
    ///     Creates the problem response used when the header value exceeds the configured maximum length.
    /// </summary>
    /// <param name="httpContext">The current HTTP request context.</param>
    /// <param name="options">The filter options carrying the length limit.</param>
    /// <returns>The key-too-long problem response.</returns>
    private static IResult CreateKeyTooLongProblem(HttpContext httpContext, IdempotencyKeyFilterOptions options)
    {
        return IdempotencyProblemResponses.Problem(
            httpContext,
            StatusCodes.Status400BadRequest,
            "Idempotency key too long",
            $"The '{options.HeaderName}' header must be {options.MaxKeyLength} characters or fewer.",
            "urn:litebus:idempotency:key-too-long");
    }
}
