using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LiteBus.Extensions.AspNetCore;

/// <summary>
///     Maps LiteBus mediation exceptions to the same problem responses mediation results write.
/// </summary>
/// <remarks>
///     <para>
///         Handles <see cref="LiteBusMessageDeniedException" /> and <see cref="LiteBusMessageInvalidException" /> by
///         resolving the refusal code through <see cref="IRefusalProblemCatalog" /> and writing the problem details
///         <see cref="MediationResultHttpExtensions" /> writes for the same refusal, through
///         <see cref="RefusalProblemDetailsFactory" /> so the two paths cannot drift. Handles
///         <see cref="MessageFaultException" /> as <c>500 Internal Server Error</c>, logged at error level. Every other
///         exception is left for the next handler.
///     </para>
///     <para>
///         The catalog is read from request services with <see cref="RefusalProblemCatalog.Default" /> as the fallback.
///         The rendering options are read from request services with a default instance as the fallback; when no
///         <see cref="RefusalProblemOptions" /> is registered, the unmapped-code policy falls back to
///         <see cref="LiteBusExceptionHandlerOptions.UnmappedRefusalCode" />. The problem is written through
///         <see cref="IProblemDetailsService" /> when the host registered it with <c>AddProblemDetails</c>; otherwise
///         it is written directly with the same shape.
///     </para>
///     <para>
///         A refusal code with no mapping follows the unmapped-code policy: the outcome default, or <c>500 Internal
///         Server Error</c> with a generic server-error type, logged at error level. A refusal without a code always
///         uses the outcome default.
///     </para>
///     <code><![CDATA[
/// services.AddLiteBusExceptionHandler(options =>
/// {
///     options.UnmappedRefusalCode = UnmappedRefusalCode.TreatAsServerError;
/// });
/// ]]></code>
/// </remarks>
public sealed class LiteBusExceptionHandler : IExceptionHandler
{
    /// <summary>
    ///     Logs a mediation fault reported as a server error.
    /// </summary>
    private static readonly Action<ILogger, string?, string?, Exception?> MediationFaultMessage =
        LoggerMessage.Define<string?, string?>(
            LogLevel.Error,
            new EventId(4102, "MediationFault"),
            "Mediation failed with fault {Code}: {Reason}.");

    /// <summary>
    ///     Decides how a refusal code with no catalog mapping is answered.
    /// </summary>
    private readonly LiteBusExceptionHandlerOptions _options;

    /// <summary>
    ///     Records mediation faults and unmapped refusal codes reported as server errors.
    /// </summary>
    private readonly ILogger<LiteBusExceptionHandler> _logger;

    /// <summary>
    ///     Initializes a new instance of the <see cref="LiteBusExceptionHandler" /> class.
    /// </summary>
    /// <param name="options">Decides how a refusal code with no catalog mapping is answered.</param>
    /// <param name="logger">Records mediation faults and unmapped refusal codes reported as server errors.</param>
    public LiteBusExceptionHandler(LiteBusExceptionHandlerOptions options, ILogger<LiteBusExceptionHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(exception);

        if (!TryDescribeRefusal(exception, out var outcome, out var reason, out var code, out var failures))
        {
            return false;
        }

        if (outcome == MediationOutcome.Failed)
        {
            MediationFaultMessage(_logger, code, reason, exception);
        }

        var catalog = ResolveCatalog(httpContext);
        var options = ResolveOptions(httpContext);

        var problem = RefusalProblemDetailsFactory.CreateProblemDetails(
            httpContext, outcome, reason, code, failures, catalog, options, _logger, exception);

        if (httpContext.RequestServices.GetService<IProblemDetailsService>() is IProblemDetailsService problemDetails)
        {
            // The problem details service writes the body only; the status line is the handler's to set.
            httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;

            await problemDetails.WriteAsync(
                new ProblemDetailsContext
                {
                    HttpContext = httpContext,
                    Exception = exception,
                    ProblemDetails = problem,
                }).ConfigureAwait(false);

            return true;
        }

        var result = Results.Problem(
            detail: problem.Detail,
            instance: problem.Instance,
            statusCode: problem.Status,
            title: problem.Title,
            type: problem.Type,
            extensions: new Dictionary<string, object?>(problem.Extensions, StringComparer.Ordinal));
        await result.ExecuteAsync(httpContext).ConfigureAwait(false);

        return true;
    }

    /// <summary>
    ///     Reads the refusal or fault a mediation exception carries, when it carries one.
    /// </summary>
    /// <param name="exception">The exception the pipeline raised.</param>
    /// <param name="outcome">How the mediation ended.</param>
    /// <param name="reason">Why the pipeline stopped, when it reported one.</param>
    /// <param name="code">The machine-readable code the decision supplied.</param>
    /// <param name="failures">The validation failures the validator stage collected.</param>
    /// <returns><see langword="true" /> when the exception is a mediation refusal or fault.</returns>
    private static bool TryDescribeRefusal(
        Exception exception,
        out MediationOutcome outcome,
        out string? reason,
        out string? code,
        out IReadOnlyList<ValidationFailure> failures)
    {
        if (exception is LiteBusMessageDeniedException denied)
        {
            outcome = MediationOutcome.Denied;
            reason = denied.Reason;
            code = denied.Code;
            failures = [];

            return true;
        }

        if (exception is LiteBusMessageInvalidException invalid)
        {
            outcome = MediationOutcome.Invalid;
            failures = invalid.Failures;

            if (failures.Count == 0)
            {
                reason = null;
                code = null;
            }
            else
            {
                reason = string.Join("; ", failures.Select(failure => failure.ToString()));
                code = failures.Count == 1 ? failures[0].Code : null;
            }

            return true;
        }

        if (exception is MessageFaultException fault)
        {
            outcome = MediationOutcome.Failed;
            reason = fault.Reason;
            code = fault.Code;
            failures = [];

            return true;
        }

        outcome = MediationOutcome.Failed;
        reason = null;
        code = null;
        failures = [];

        return false;
    }

    /// <summary>
    ///     Resolves the catalog a refusal is mapped through.
    /// </summary>
    /// <param name="httpContext">The current HTTP request context.</param>
    /// <returns>The request-services catalog, or the default catalog.</returns>
    private static IRefusalProblemCatalog ResolveCatalog(HttpContext httpContext)
    {
        if (httpContext.RequestServices.GetService<IRefusalProblemCatalog>() is IRefusalProblemCatalog registered)
        {
            return registered;
        }

        return RefusalProblemCatalog.Default;
    }

    /// <summary>
    ///     Resolves the options a refusal is rendered with.
    /// </summary>
    /// <param name="httpContext">The current HTTP request context.</param>
    /// <returns>
    ///     The request-services options, or default options carrying this handler's unmapped-code policy when none is
    ///     registered.
    /// </returns>
    private RefusalProblemOptions ResolveOptions(HttpContext httpContext)
    {
        if (httpContext.RequestServices.GetService<RefusalProblemOptions>() is RefusalProblemOptions registered)
        {
            return registered;
        }

        return new RefusalProblemOptions { UnmappedRefusalCode = _options.UnmappedRefusalCode };
    }
}
