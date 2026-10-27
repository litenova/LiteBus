using System;
using LiteBus.Messaging.Abstractions;
using Microsoft.AspNetCore.Http;

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
    /// <returns>The success response, or the problem response the refusal maps to.</returns>
    /// <remarks>
    ///     <para>
    ///         An idempotency refusal carries its own status code: a key another delivery is still applying answers
    ///         <c>409 Conflict</c> so the client retries, and a key already used for a different request answers
    ///         <c>422 Unprocessable Content</c> because retrying the same request cannot help. Any other denial answers
    ///         <c>403 Forbidden</c> and a validation failure answers <c>400 Bad Request</c>, both carrying the reason
    ///         the pipeline reported.
    ///     </para>
    ///     <code><![CDATA[
    /// var result = await mediator.TrySendAsync(command, settings, cancellationToken).ConfigureAwait(false);
    /// return result.ToHttpResult(context, () => Results.Ok());
    /// ]]></code>
    /// </remarks>
    public static IResult ToHttpResult(this MediationResult result, HttpContext httpContext, Func<IResult> onSuccess)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(onSuccess);

        if (result.IsSuccess)
        {
            return onSuccess();
        }

        return ToRefusalProblem(result.Outcome, result.Reason, result.Code, httpContext);
    }

    /// <summary>
    ///     Turns the outcome of a result-producing command mediation into an HTTP response in one call.
    /// </summary>
    /// <typeparam name="TMessageResult">The type of result the command produces.</typeparam>
    /// <param name="result">How the mediation ended, carrying the value when it produced one.</param>
    /// <param name="httpContext">The current HTTP request context.</param>
    /// <param name="onSuccess">Builds the response from the produced value.</param>
    /// <returns>The success response, or the problem response the refusal maps to.</returns>
    /// <remarks>
    ///     The value is the recorded result when the idempotency shortcut answered a repeat, so a retried request
    ///     receives the same answer as the first one. Refusals map exactly as the untyped overload maps them.
    /// </remarks>
    public static IResult ToHttpResult<TMessageResult>(
        this MediationResult<TMessageResult> result,
        HttpContext httpContext,
        Func<TMessageResult?, IResult> onSuccess)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(onSuccess);

        if (result.IsSuccess)
        {
            return onSuccess(result.Value);
        }

        return ToRefusalProblem(result.Outcome, result.Reason, result.Code, httpContext);
    }

    /// <summary>
    ///     Maps a refusal to the problem response its outcome and code call for.
    /// </summary>
    /// <param name="outcome">How the mediation ended.</param>
    /// <param name="reason">Why the pipeline stopped, when it reported one.</param>
    /// <param name="code">The machine-readable code the decision supplied.</param>
    /// <param name="httpContext">The current HTTP request context.</param>
    /// <returns>The problem response.</returns>
    private static IResult ToRefusalProblem(
        MediationOutcome outcome,
        string? reason,
        string? code,
        HttpContext httpContext)
    {
        return code switch
        {
            IdempotencyRefusalCodes.InFlight => IdempotencyProblemResponses.Problem(
                httpContext,
                StatusCodes.Status409Conflict,
                "Idempotent request in flight",
                "A request with the same idempotency key is still being processed. Retry the request.",
                "urn:litebus:idempotency:in-flight"),
            IdempotencyRefusalCodes.FingerprintMismatch => IdempotencyProblemResponses.Problem(
                httpContext,
                StatusCodes.Status422UnprocessableEntity,
                "Idempotency key already used",
                "The idempotency key was already used for a different request.",
                "urn:litebus:idempotency:fingerprint-mismatch"),
            _ => outcome == MediationOutcome.Invalid
                ? IdempotencyProblemResponses.Problem(
                    httpContext,
                    StatusCodes.Status400BadRequest,
                    "Invalid request",
                    reason ?? "The request failed validation.",
                    "urn:litebus:mediation:invalid")
                : IdempotencyProblemResponses.Problem(
                    httpContext,
                    StatusCodes.Status403Forbidden,
                    "Request denied",
                    reason ?? "The request was denied.",
                    "urn:litebus:mediation:denied"),
        };
    }
}
