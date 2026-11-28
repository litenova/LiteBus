using Microsoft.AspNetCore.Mvc;

namespace LiteBus.Extensions.AspNetCore;

/// <summary>
///     Adds application-specific content to a problem response, such as localized titles or projected members.
/// </summary>
/// <remarks>
///     <para>
///         An enricher runs after the catalog status and title, the detail sentence, the problem type, the refusal
///         code, and the validation errors are written, so it can replace or project any of them. LiteBus ships no
///         string tables: localization belongs to the application behind this port.
///     </para>
/// </remarks>
public interface IProblemDetailsEnricher
{
    /// <summary>
    ///     Adds application-specific content to a problem response.
    /// </summary>
    /// <param name="problem">The problem response to extend.</param>
    /// <param name="context">The refusal the problem response was built from.</param>
    void Enrich(ProblemDetails problem, ProblemDetailsEnrichmentContext context);
}
