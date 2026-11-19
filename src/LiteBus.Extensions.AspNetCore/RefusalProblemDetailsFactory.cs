using System;
using System.Collections.Generic;
using LiteBus.Messaging.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LiteBus.Extensions.AspNetCore;

/// <summary>
///     Builds the problem details a refusal maps to, shared by mediation results and the exception handler.
/// </summary>
/// <remarks>
///     <para>
///         Both <see cref="MediationResultHttpExtensions" /> and <see cref="LiteBusExceptionHandler" /> write through
///         this factory, so the two paths cannot drift: the catalog status and title, the detail sentence, the stable
///         <c>urn:litebus:</c> type, the request path as the instance, the trace identifier, and the validation errors
///         extension are built once.
///     </para>
/// </remarks>
internal static class RefusalProblemDetailsFactory
{
    /// <summary>
    ///     Creates the problem details for a refusal.
    /// </summary>
    /// <param name="httpContext">The current HTTP request context.</param>
    /// <param name="outcome">How the mediation ended.</param>
    /// <param name="reason">Why the pipeline stopped, when it reported one.</param>
    /// <param name="code">The machine-readable code the decision supplied.</param>
    /// <param name="failures">The validation failures the validator stage collected.</param>
    /// <param name="mapping">The catalog mapping for the code.</param>
    /// <returns>The problem details.</returns>
    internal static ProblemDetails CreateProblemDetails(
        HttpContext httpContext,
        MediationOutcome outcome,
        string? reason,
        string? code,
        IReadOnlyList<ValidationFailure> failures,
        RefusalProblemMap mapping)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(failures);
        ArgumentNullException.ThrowIfNull(mapping);

        var problem = new ProblemDetails
        {
            Status = mapping.StatusCode,
            Title = mapping.Title,
            Detail = BuildDetail(outcome, reason, code),
            Type = BuildProblemType(outcome, code),
            Instance = httpContext.Request.Path.Value,
        };
        problem.Extensions["traceId"] = httpContext.TraceIdentifier;

        if (outcome == MediationOutcome.Invalid && failures.Count > 0)
        {
            problem.Extensions["errors"] = BuildErrors(failures);
        }

        return problem;
    }

    /// <summary>
    ///     Creates the result response for a refusal.
    /// </summary>
    /// <param name="httpContext">The current HTTP request context.</param>
    /// <param name="outcome">How the mediation ended.</param>
    /// <param name="reason">Why the pipeline stopped, when it reported one.</param>
    /// <param name="code">The machine-readable code the decision supplied.</param>
    /// <param name="failures">The validation failures the validator stage collected.</param>
    /// <param name="mapping">The catalog mapping for the code.</param>
    /// <returns>The problem response.</returns>
    internal static IResult CreateResult(
        HttpContext httpContext,
        MediationOutcome outcome,
        string? reason,
        string? code,
        IReadOnlyList<ValidationFailure> failures,
        RefusalProblemMap mapping)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(failures);
        ArgumentNullException.ThrowIfNull(mapping);

        var problem = CreateProblemDetails(httpContext, outcome, reason, code, failures, mapping);

        return Results.Problem(
            detail: problem.Detail,
            instance: problem.Instance,
            statusCode: problem.Status,
            title: problem.Title,
            type: problem.Type,
            extensions: new Dictionary<string, object?>(problem.Extensions, StringComparer.Ordinal));
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

    /// <summary>
    ///     Groups validation failures by member for the errors extension.
    /// </summary>
    /// <param name="failures">The validation failures the validator stage collected.</param>
    /// <returns>The error messages keyed by member, with message-level failures under an empty key.</returns>
    private static Dictionary<string, string[]> BuildErrors(IReadOnlyList<ValidationFailure> failures)
    {
        var grouped = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var failure in failures)
        {
            var key = failure.Member ?? string.Empty;

            if (!grouped.TryGetValue(key, out var messages))
            {
                messages = [];
                grouped[key] = messages;
            }

            messages.Add(failure.Message);
        }

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        foreach (var (key, messages) in grouped)
        {
            errors[key] = [.. messages];
        }

        return errors;
    }
}
