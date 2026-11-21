using System;
using Microsoft.AspNetCore.Mvc;

namespace LiteBus.Extensions.AspNetCore;

/// <summary>
///     The enricher that leaves every problem response unchanged.
/// </summary>
public sealed class NullProblemDetailsEnricher : IProblemDetailsEnricher
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="NullProblemDetailsEnricher" /> class.
    /// </summary>
    public NullProblemDetailsEnricher()
    {
    }

    /// <summary>
    ///     Gets the shared enricher used unless the application configures another one.
    /// </summary>
    public static NullProblemDetailsEnricher Default { get; } = new();

    /// <inheritdoc />
    public void Enrich(ProblemDetails problem, ProblemDetailsEnrichmentContext context)
    {
        ArgumentNullException.ThrowIfNull(problem);
        ArgumentNullException.ThrowIfNull(context);
    }
}
