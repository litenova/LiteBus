using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Runtime.Abstractions.Diagnostics;

namespace LiteBus.Extensions.AspNetCore;

/// <summary>
///     Reports the refusal code mappings every problem response is rendered from.
/// </summary>
/// <remarks>
///     <para>
///         Lists each mapped code and prefix pattern with the contributor that registered it, plus the outcome defaults
///         for invalid and denied refusals, so composition explains which module owns each answer. A host that has not
///         registered a catalog answers from the shared default catalog, which the probe reports instead of failing.
///     </para>
/// </remarks>
public sealed class RefusalProblemCatalogDiagnosticCheck : IDiagnosticCheck
{
    /// <summary>
    ///     The name reported by this probe.
    /// </summary>
    public const string CheckName = "litebus.problem-catalog";

    /// <summary>
    ///     Resolves the catalog without requiring it, so a missing registration is reported rather than thrown.
    /// </summary>
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    ///     Initializes a new instance of the <see cref="RefusalProblemCatalogDiagnosticCheck" /> class.
    /// </summary>
    /// <param name="serviceProvider">The provider used to look for the refusal problem catalog.</param>
    public RefusalProblemCatalogDiagnosticCheck(IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        _serviceProvider = serviceProvider;
    }

    /// <inheritdoc />
    public string Name => CheckName;

    /// <inheritdoc />
    public Task<DiagnosticResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var catalog = _serviceProvider.GetService(typeof(IRefusalProblemCatalog)) as IRefusalProblemCatalog;

        if (catalog is null)
        {
            return Task.FromResult(new DiagnosticResult(
                DiagnosticStatus.Healthy,
                "No refusal problem catalog is registered, so refusals answer from the shared default catalog.",
                new Dictionary<string, object>
                {
                    ["component"] = "problem-catalog",
                    ["registered"] = false,
                }));
        }

        if (catalog is not RefusalProblemCatalog concrete)
        {
            return Task.FromResult(new DiagnosticResult(
                DiagnosticStatus.Healthy,
                "A custom refusal problem catalog is registered, so its mappings are not listed in the export.",
                new Dictionary<string, object>
                {
                    ["component"] = "problem-catalog",
                    ["registered"] = true,
                    ["customCatalog"] = true,
                }));
        }

        var export = concrete.Export();
        var contributors = export.Mappings
            .Select(entry => entry.Contributor ?? "unknown")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        return Task.FromResult(new DiagnosticResult(
            DiagnosticStatus.Healthy,
            $"The refusal problem catalog carries {export.Mappings.Count} mappings from {contributors.Length} contributors. "
            + $"Invalid refusals without a code mapping answer {export.InvalidDefault.StatusCode} '{export.InvalidDefault.Title}', "
            + $"and denied refusals without a code mapping answer {export.DeniedDefault.StatusCode} '{export.DeniedDefault.Title}'.",
            new Dictionary<string, object>
            {
                ["component"] = "problem-catalog",
                ["registered"] = true,
                ["customCatalog"] = false,
                ["mappingCount"] = export.Mappings.Count,
                ["patterns"] = export.Mappings.Select(entry => entry.Pattern).ToArray(),
                ["contributors"] = contributors,
                ["invalidDefault"] = $"{export.InvalidDefault.StatusCode} {export.InvalidDefault.Title}",
                ["deniedDefault"] = $"{export.DeniedDefault.StatusCode} {export.DeniedDefault.Title}",
            }));
    }
}
