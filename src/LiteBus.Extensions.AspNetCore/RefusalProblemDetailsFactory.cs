using System;
using System.Collections.Generic;
using LiteBus.Messaging.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace LiteBus.Extensions.AspNetCore;

/// <summary>
///     Builds the problem details a refusal maps to, shared by mediation results and the exception handler.
/// </summary>
/// <remarks>
///     <para>
///         Both <see cref="MediationResultHttpExtensions" /> and <see cref="LiteBusExceptionHandler" /> write through
///         this factory, so the two paths cannot drift: the catalog status and title, the detail sentence, the problem
///         type, the request path as the instance, the trace identifier, the refusal code, and the validation errors
///         extension are built once, and the per-outcome unmapped-code policy applies on both paths. An unmapped code
///         answered from an outcome default is still reported: a denied code logs at warning level and an invalid
///         code at debug level, because per-validator codes are unmapped by design.
///     </para>
/// </remarks>
internal static class RefusalProblemDetailsFactory
{
    /// <summary>
    ///     The title answered when the mediation failed or an unmapped refusal is reported as a server error.
    /// </summary>
    private const string ServerErrorTitle = "Internal server error";

    /// <summary>
    ///     The problem type answered when an unmapped refusal is reported as a server error.
    /// </summary>
    private const string UnmappedServerErrorType = "about:blank";

    /// <summary>
    ///     Logs a refusal code that has no problem mapping and is reported as a server error.
    /// </summary>
    private static readonly Action<ILogger, string?, Exception?> UnmappedRefusalCodeMessage =
        LoggerMessage.Define<string?>(
            LogLevel.Error,
            new EventId(4101, "UnmappedRefusalCode"),
            "The refusal code {Code} has no problem mapping, so the refusal is reported as a server error.");

    /// <summary>
    ///     Logs a denied code that has no problem mapping and is answered from the denied default.
    /// </summary>
    private static readonly Action<ILogger, string?, int, Exception?> UnmappedDeniedFallbackMessage =
        LoggerMessage.Define<string?, int>(
            LogLevel.Warning,
            new EventId(4103, "UnmappedDeniedCode"),
            "The refusal code {Code} has no problem mapping, so the denial is answered from the denied default ({StatusCode}).");

    /// <summary>
    ///     Logs a validation code that has no problem mapping and is answered from the invalid default.
    /// </summary>
    private static readonly Action<ILogger, string?, int, Exception?> UnmappedInvalidFallbackMessage =
        LoggerMessage.Define<string?, int>(
            LogLevel.Debug,
            new EventId(4104, "UnmappedValidationCode"),
            "The validation code {Code} has no problem mapping, so the invalid refusal is answered from the invalid default ({StatusCode}).");

    /// <summary>
    ///     Creates the problem details for a refusal.
    /// </summary>
    /// <param name="httpContext">The current HTTP request context.</param>
    /// <param name="outcome">How the mediation ended.</param>
    /// <param name="reason">Why the pipeline stopped, when it reported one.</param>
    /// <param name="code">The machine-readable code the decision supplied.</param>
    /// <param name="failures">The validation failures the validator stage collected.</param>
    /// <param name="catalog">The catalog the code is resolved through.</param>
    /// <param name="options">How the refusal is rendered as a problem response.</param>
    /// <param name="logger">The logger for unmapped refusal codes, or <see langword="null" /> when none is available.</param>
    /// <param name="exception">The exception the refusal was read from, when it was read from one.</param>
    /// <returns>The problem details.</returns>
    internal static ProblemDetails CreateProblemDetails(
        HttpContext httpContext,
        MediationOutcome outcome,
        string? reason,
        string? code,
        IReadOnlyList<ValidationFailure> failures,
        IRefusalProblemCatalog catalog,
        RefusalProblemOptions options,
        ILogger? logger,
        Exception? exception)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(failures);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.TypeFormatter);
        ArgumentNullException.ThrowIfNull(options.ValidationErrors);
        ArgumentNullException.ThrowIfNull(options.Enricher);

        var mapping = ResolveMapping(code, outcome, catalog, options, logger, exception, out var unmappedServerError);

        var problem = new ProblemDetails
        {
            Status = mapping.StatusCode,
            Title = mapping.Title,
            Detail = BuildDetail(outcome, reason, code),
            Type = unmappedServerError ? UnmappedServerErrorType : options.TypeFormatter.Format(code, outcome),
            Instance = httpContext.Request.Path.Value,
        };
        problem.Extensions["traceId"] = httpContext.TraceIdentifier;

        if (options.IncludeRefusalCode && !string.IsNullOrWhiteSpace(code))
        {
            problem.Extensions["code"] = code;
        }

        if (outcome == MediationOutcome.Invalid && failures.Count > 0)
        {
            options.ValidationErrors.WriteErrors(problem, failures);
        }

        options.Enricher.Enrich(problem, new ProblemDetailsEnrichmentContext(httpContext, outcome, code, reason));

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
    /// <param name="catalog">The catalog the code is resolved through.</param>
    /// <param name="options">How the refusal is rendered as a problem response.</param>
    /// <param name="logger">The logger for unmapped refusal codes, or <see langword="null" /> when none is available.</param>
    /// <param name="exception">The exception the refusal was read from, when it was read from one.</param>
    /// <returns>The problem response.</returns>
    internal static IResult CreateResult(
        HttpContext httpContext,
        MediationOutcome outcome,
        string? reason,
        string? code,
        IReadOnlyList<ValidationFailure> failures,
        IRefusalProblemCatalog catalog,
        RefusalProblemOptions options,
        ILogger? logger,
        Exception? exception)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(failures);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(options);

        var problem = CreateProblemDetails(
            httpContext, outcome, reason, code, failures, catalog, options, logger, exception);

        return Results.Problem(
            detail: problem.Detail,
            instance: problem.Instance,
            statusCode: problem.Status,
            title: problem.Title,
            type: problem.Type,
            extensions: new Dictionary<string, object?>(problem.Extensions, StringComparer.Ordinal));
    }

    /// <summary>
    ///     Resolves the mapping for a refusal, applying the per-outcome unmapped-code policy.
    /// </summary>
    /// <param name="code">The machine-readable code the decision supplied.</param>
    /// <param name="outcome">How the mediation ended.</param>
    /// <param name="catalog">The catalog the code is resolved through.</param>
    /// <param name="options">How the refusal is rendered as a problem response.</param>
    /// <param name="logger">The logger for unmapped refusal codes, or <see langword="null" /> when none is available.</param>
    /// <param name="exception">The exception the refusal was read from, when it was read from one.</param>
    /// <param name="unmappedServerError">Whether an unmapped code is reported as a server error.</param>
    /// <returns>The mapping the problem response is built from.</returns>
    private static RefusalProblemMap ResolveMapping(
        string? code,
        MediationOutcome outcome,
        IRefusalProblemCatalog catalog,
        RefusalProblemOptions options,
        ILogger? logger,
        Exception? exception,
        out bool unmappedServerError)
    {
        if (outcome == MediationOutcome.Failed)
        {
            unmappedServerError = false;

            return new RefusalProblemMap(StatusCodes.Status500InternalServerError, ServerErrorTitle);
        }

        var isMapped = catalog.TryResolve(code, outcome, out var mapping);

        var treatAsServerError = outcome == MediationOutcome.Invalid
            ? options.UnmappedValidationCode == UnmappedValidationCode.TreatAsServerError
            : options.UnmappedRefusalCode == UnmappedRefusalCode.TreatAsServerError;

        if (!string.IsNullOrWhiteSpace(code)
            && !isMapped
            && treatAsServerError)
        {
            unmappedServerError = true;

            if (logger is not null)
            {
                UnmappedRefusalCodeMessage(logger, code, exception);
            }

            return new RefusalProblemMap(StatusCodes.Status500InternalServerError, ServerErrorTitle);
        }

        unmappedServerError = false;

        if (logger is not null && !string.IsNullOrWhiteSpace(code) && !isMapped)
        {
            if (outcome == MediationOutcome.Denied)
            {
                UnmappedDeniedFallbackMessage(logger, code, mapping.StatusCode, exception);
            }
            else if (outcome == MediationOutcome.Invalid)
            {
                UnmappedInvalidFallbackMessage(logger, code, mapping.StatusCode, exception);
            }
        }

        return mapping;
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
            _ => reason ?? outcome switch
            {
                MediationOutcome.Invalid => "The request failed validation.",
                MediationOutcome.Failed => "The mediation failed with a fault.",
                _ => "The request was denied.",
            },
        };
    }
}
