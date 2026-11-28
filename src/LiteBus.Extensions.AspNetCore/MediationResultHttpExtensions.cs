using System;
using System.Collections.Generic;
using LiteBus.Messaging.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LiteBus.Extensions.AspNetCore;

/// <summary>
///     Turns the outcome a <c>TrySendAsync</c> call reports into an HTTP response.
/// </summary>
public static class MediationResultHttpExtensions
{
    /// <summary>
    ///     Turns the outcome of a command mediation into an HTTP response in one call.
    /// </summary>
    /// <param name="result">How the mediation ended.</param>
    /// <param name="httpContext">The current HTTP request context.</param>
    /// <param name="onSuccess">Builds the response for a mediation that ran or was answered from the store.</param>
    /// <param name="catalog">The refusal catalog, or <see langword="null" /> to resolve it from request services with <see cref="RefusalProblemCatalog.Default" /> as the fallback.</param>
    /// <param name="options">How the refusal is rendered as a problem response, or <see langword="null" /> to resolve it from request services with default options as the fallback.</param>
    /// <returns>The success response, or the problem response the refusal maps to.</returns>
    /// <remarks>
    ///     <para>
    ///         The catalog maps the refusal code to its status code and title: a missing declaration, an
    ///         authorization denial, and a missing tenant answer <c>403 Forbidden</c>; a caller that is not signed in
    ///         answers <c>401 Unauthorized</c>; a refusal whose code starts with <c>validation.</c> answers
    ///         <c>422 Unprocessable Content</c>; a concurrency conflict answers <c>409 Conflict</c>; and anything else,
    ///         including an invalid result whose code has no mapping, answers the outcome default, 400 unless the host
    ///         changed it. The
    ///         problem carries the reason the pipeline reported, the refusal code, and a stable problem type, so one
    ///         client handler reads every refusal the same way.
    ///     </para>
    ///     <para>
    ///         An idempotency refusal keeps its own detail: a key another delivery is still applying answers
    ///         <c>409 Conflict</c> so the client retries, and a key already used for a different request answers
    ///         <c>422 Unprocessable Content</c> because retrying the same request cannot help.
    ///     </para>
    ///     <para>
    ///         An invalid result carries an <c>errors</c> extension keyed by member, with message-level failures under
    ///         an empty key, in the same shape as <c>ValidationProblemDetails.Errors</c>. The catalog status, title,
    ///         and type stay as mapped.
    ///     </para>
    ///     <para>
    ///         A refusal code with no mapping follows the unmapped-code policy: the outcome default, or <c>500
    ///         Internal Server Error</c> with a generic server-error type, logged at error level. The exception path in
    ///         <see cref="LiteBusExceptionHandler" /> applies the same policy through the same shared mapping.
    ///     </para>
    ///     <code><![CDATA[
    /// var result = await mediator.TrySendAsync(command, settings, cancellationToken).ConfigureAwait(false);
    /// return result.ToHttpResult(context, () => Results.Ok());
    /// ]]></code>
    /// </remarks>
    public static IResult ToHttpResult(
        this MediationResult result,
        HttpContext httpContext,
        Func<IResult> onSuccess,
        IRefusalProblemCatalog? catalog = null,
        RefusalProblemOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(onSuccess);

        if (result.IsSuccess)
        {
            return onSuccess();
        }

        return ToRefusalProblem(
            result.Outcome,
            result.Reason,
            result.Code,
            result.Failures,
            httpContext,
            ResolveCatalog(httpContext, catalog),
            ResolveOptions(httpContext, options));
    }

    /// <summary>
    ///     Turns the outcome of a result-producing command mediation into an HTTP response in one call.
    /// </summary>
    /// <typeparam name="TMessageResult">The type of result the command produces.</typeparam>
    /// <param name="result">How the mediation ended, carrying the value when it produced one.</param>
    /// <param name="httpContext">The current HTTP request context.</param>
    /// <param name="onSuccess">Builds the response from the produced value.</param>
    /// <param name="catalog">The refusal catalog, or <see langword="null" /> to resolve it from request services with <see cref="RefusalProblemCatalog.Default" /> as the fallback.</param>
    /// <param name="options">How the refusal is rendered as a problem response, or <see langword="null" /> to resolve it from request services with default options as the fallback.</param>
    /// <returns>The success response, or the problem response the refusal maps to.</returns>
    /// <remarks>
    ///     The value is the recorded result when the idempotency shortcut answered a repeat, so a retried request
    ///     receives the same answer as the first one. Refusals map exactly as the untyped overload maps them, including
    ///     the <c>errors</c> extension on an invalid result and the unmapped-code policy.
    /// </remarks>
    public static IResult ToHttpResult<TMessageResult>(
        this MediationResult<TMessageResult> result,
        HttpContext httpContext,
        Func<TMessageResult?, IResult> onSuccess,
        IRefusalProblemCatalog? catalog = null,
        RefusalProblemOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(onSuccess);

        if (result.IsSuccess)
        {
            return onSuccess(result.Value);
        }

        return ToRefusalProblem(
            result.Outcome,
            result.Reason,
            result.Code,
            result.Failures,
            httpContext,
            ResolveCatalog(httpContext, catalog),
            ResolveOptions(httpContext, options));
    }

    /// <summary>
    ///     Resolves the catalog a refusal is mapped through.
    /// </summary>
    /// <param name="httpContext">The current HTTP request context.</param>
    /// <param name="catalog">The caller-supplied catalog, if any.</param>
    /// <returns>The caller-supplied catalog, the request-services catalog, or the default catalog.</returns>
    private static IRefusalProblemCatalog ResolveCatalog(HttpContext httpContext, IRefusalProblemCatalog? catalog)
    {
        if (catalog is not null)
        {
            return catalog;
        }

        if (httpContext.RequestServices is IServiceProvider services
            && services.GetService<IRefusalProblemCatalog>() is IRefusalProblemCatalog registered)
        {
            return registered;
        }

        return RefusalProblemCatalog.Default;
    }

    /// <summary>
    ///     Resolves the options a refusal is rendered with.
    /// </summary>
    /// <param name="httpContext">The current HTTP request context.</param>
    /// <param name="options">The caller-supplied options, if any.</param>
    /// <returns>The caller-supplied options, the request-services options, or default options.</returns>
    private static RefusalProblemOptions ResolveOptions(HttpContext httpContext, RefusalProblemOptions? options)
    {
        if (options is not null)
        {
            return options;
        }

        if (httpContext.RequestServices is IServiceProvider services
            && services.GetService<RefusalProblemOptions>() is RefusalProblemOptions registered)
        {
            return registered;
        }

        return new RefusalProblemOptions();
    }

    /// <summary>
    ///     Resolves the logger for unmapped refusal codes.
    /// </summary>
    /// <param name="httpContext">The current HTTP request context.</param>
    /// <returns>The catalog logger, or <see langword="null" /> when logging is not registered.</returns>
    private static ILogger? ResolveLogger(HttpContext httpContext)
    {
        if (httpContext.RequestServices is IServiceProvider services)
        {
            return services.GetService<ILogger<RefusalProblemCatalog>>();
        }

        return null;
    }

    /// <summary>
    ///     Maps a refusal to the problem response its code calls for.
    /// </summary>
    /// <param name="outcome">How the mediation ended.</param>
    /// <param name="reason">Why the pipeline stopped, when it reported one.</param>
    /// <param name="code">The machine-readable code the decision supplied.</param>
    /// <param name="failures">The validation failures the validator stage collected.</param>
    /// <param name="httpContext">The current HTTP request context.</param>
    /// <param name="catalog">The catalog the code is resolved through.</param>
    /// <param name="options">How the refusal is rendered as a problem response.</param>
    /// <returns>The problem response.</returns>
    private static IResult ToRefusalProblem(
        MediationOutcome outcome,
        string? reason,
        string? code,
        IReadOnlyList<ValidationFailure> failures,
        HttpContext httpContext,
        IRefusalProblemCatalog catalog,
        RefusalProblemOptions options)
    {
        return RefusalProblemDetailsFactory.CreateResult(
            httpContext, outcome, reason, code, failures, catalog, options, ResolveLogger(httpContext), exception: null);
    }
}
