using LiteBus.Messaging.Abstractions;
using Microsoft.AspNetCore.Http;

namespace LiteBus.Extensions.AspNetCore;

/// <summary>
///     The refusal a problem response was built from, handed to an enricher.
/// </summary>
/// <param name="HttpContext">The current HTTP request context.</param>
/// <param name="Outcome">How the mediation ended.</param>
/// <param name="Code">The machine-readable code the decision supplied, when it supplied one.</param>
/// <param name="Reason">Why the pipeline stopped, when it reported one.</param>
public sealed record ProblemDetailsEnrichmentContext(
    HttpContext HttpContext,
    MediationOutcome Outcome,
    string? Code,
    string? Reason);
