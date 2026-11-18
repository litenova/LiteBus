using System;
using LiteBus.Messaging.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

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
    /// <returns>The success response, or the problem response the refusal maps to.</returns>
    /// <remarks>
    ///     <para>
    ///         The catalog maps the refusal code to its status code and title: a missing declaration, an
    ///         authorization denial, and a missing tenant answer <c>403 Forbidden</c>; a caller that is not signed in
    ///         answers <c>401 Unauthorized</c>; a validation failure answers <c>422 Unprocessable Content</c>; a
    ///         concurrency conflict answers <c>409 Conflict</c>; and anything else answers <c>400 Bad Request</c>. The
    ///         problem carries the reason the pipeline reported and a stable <c>urn:litebus:</c> type derived from the
    ///         code, so one client handler reads every refusal the same way.
    ///     </para>
    ///     <para>
    ///         An idempotency refusal keeps its own detail: a key another delivery is still applying answers
    ///         <c>409 Conflict</c> so the client retries, and a key already used for a different request answers
    ///         <c>422 Unprocessable Content</c> because retrying the same request cannot help.
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
        IRefusalProblemCatalog? catalog = null)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(onSuccess);

        if (result.IsSuccess)
        {
            return onSuccess();
        }

        return ToRefusalProblem(result.Outcome, result.Reason, result.Code, httpContext, ResolveCatalog(httpContext, catalog));
    }

    /// <summary>
    ///     Turns the outcome of a result-producing command mediation into an HTTP response in one call.
    /// </summary>
    /// <typeparam name="TMessageResult">The type of result the command produces.</typeparam>
    /// <param name="result">How the mediation ended, carrying the value when it produced one.</param>
    /// <param name="httpContext">The current HTTP request context.</param>
    /// <param name="onSuccess">Builds the response from the produced value.</param>
    /// <param name="catalog">The refusal catalog, or <see langword="null" /> to resolve it from request services with <see cref="RefusalProblemCatalog.Default" /> as the fallback.</param>
    /// <returns>The success response, or the problem response the refusal maps to.</returns>
    /// <remarks>
    ///     The value is the recorded result when the idempotency shortcut answered a repeat, so a retried request
    ///     receives the same answer as the first one. Refusals map exactly as the untyped overload maps them.
    /// </remarks>
    public static IResult ToHttpResult<TMessageResult>(
        this MediationResult<TMessageResult> result,
        HttpContext httpContext,
        Func<TMessageResult?, IResult> onSuccess,
        IRefusalProblemCatalog? catalog = null)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(onSuccess);

        if (result.IsSuccess)
        {
            return onSuccess(result.Value);
        }

        return ToRefusalProblem(result.Outcome, result.Reason, result.Code, httpContext, ResolveCatalog(httpContext, catalog));
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
    ///     Maps a refusal to the problem response its code calls for.
    /// </summary>
    /// <param name="outcome">How the mediation ended.</param>
    /// <param name="reason">Why the pipeline stopped, when it reported one.</param>
    /// <param name="code">The machine-readable code the decision supplied.</param>
    /// <param name="httpContext">The current HTTP request context.</param>
    /// <param name="catalog">The catalog the code is resolved through.</param>
    /// <returns>The problem response.</returns>
    private static IResult ToRefusalProblem(
        MediationOutcome outcome,
        string? reason,
        string? code,
        HttpContext httpContext,
        IRefusalProblemCatalog catalog)
    {
        var mapping = catalog.Resolve(code);

        return IdempotencyProblemResponses.Problem(
            httpContext,
            mapping.StatusCode,
            mapping.Title,
            BuildDetail(outcome, reason, code),
            BuildProblemType(outcome, code));
    }

    /// <summary>
    ///     Builds the problem detail for a refusal.
    /// </summary>
    /// <param name="outcome">How the mediation ended.</param>
    /// <param name="reason">Why the pipeline stopped, when it reported one.</param>
    /// <param name="code">The machine-readable code the decision supplied.</param>
    /// <returns>The safe problem detail.</returns>
    private static string BuildDetail(MediationOutcome outcome, string? reason, string? code)
    {
        return code switch
        {
            IdempotencyRefusalCodes.InFlight =>
                "A request with the same idempotency key is still being processed. Retry the request.",
            IdempotencyRefusalCodes.FingerprintMismatch =>
                "The idempotency key was already used for a different request.",
            _ => reason ?? (outcome == MediationOutcome.Invalid
                ? "The request failed validation."
                : "The request was denied."),
        };
    }

    /// <summary>
    ///     Builds the stable problem type for a refusal.
    /// </summary>
    /// <param name="outcome">How the mediation ended.</param>
    /// <param name="code">The machine-readable code the decision supplied.</param>
    /// <returns>The stable problem type.</returns>
    private static string BuildProblemType(MediationOutcome outcome, string? code)
    {
        if (!string.IsNullOrWhiteSpace(code))
        {
            return "urn:litebus:" + code.Replace(".", ":", StringComparison.Ordinal);
        }

        return outcome == MediationOutcome.Invalid
            ? "urn:litebus:mediation:invalid"
            : "urn:litebus:mediation:denied";
    }
}
