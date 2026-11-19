using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Diagnostics;

namespace LiteBus.Messaging.Registry;

/// <summary>
///     Lists the messages that state no position on a report-mode declaration requirement.
/// </summary>
/// <remarks>
///     <para>
///         A failing requirement can only land atomically with the last declaration, because the first missing
///         position fails the whole composition. Reporting the gap as degraded instead lets a team adopt positions
///         message by message: the probe lists what remains, and the requirement changes to fail enforcement once the
///         list is empty.
///     </para>
///     <para>
///         Every resolution happens inside a dispatch scope rather than against the provider the probe was given. The
///         catalog is a singleton, so either would answer, but resolving through the scope keeps the probe consistent
///         with the other probes when a container validates scopes.
///     </para>
/// </remarks>
public sealed class DeclarationReportDiagnosticCheck : IDiagnosticCheck
{
    /// <summary>
    ///     The name reported by this probe.
    /// </summary>
    public const string CheckName = "litebus.declarations";

    /// <summary>
    ///     Resolves the catalog and the report without requiring them, so a missing registration is reported rather
    ///     than thrown.
    /// </summary>
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    ///     Initializes a new instance of the <see cref="DeclarationReportDiagnosticCheck" /> class.
    /// </summary>
    /// <param name="serviceProvider">The provider used to look for the catalog and the declaration report.</param>
    public DeclarationReportDiagnosticCheck(IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        _serviceProvider = serviceProvider;
    }

    /// <inheritdoc />
    public string Name => CheckName;

    /// <inheritdoc />
    public Task<DiagnosticResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var catalog = Resolve<IMessageCatalog>();
        var report = Resolve<DeclarationReport>();

        if (catalog is null || report is null)
        {
            return Task.FromResult(new DiagnosticResult(
                DiagnosticStatus.Unhealthy,
                "The declaration report cannot be evaluated because "
                + (catalog is null ? "no IMessageCatalog is registered." : "no declaration report is registered.")
                + " Register the probe through a report-mode RequireDeclaration rather than by hand.",
                new Dictionary<string, object>
                {
                    ["component"] = "declarations",
                    ["catalogRegistered"] = catalog is not null,
                    ["reportRegistered"] = report is not null
                }));
        }

        Dictionary<DeclarationRequirement, List<string>> undeclared = [];

        foreach (var requirement in report.Requirements)
        {
            var missing = catalog
                .MissingDeclaration(requirement.ValueType, requirement.Scope)
                .Select(entry => entry.MessageType.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();

            if (missing.Count > 0)
            {
                undeclared[requirement] = missing;
            }
        }

        if (undeclared.Count == 0)
        {
            return Task.FromResult(new DiagnosticResult(
                DiagnosticStatus.Healthy,
                "Every registered message states a position on every reported declaration.",
                new Dictionary<string, object>
                {
                    ["component"] = "declarations",
                    ["undeclaredCount"] = 0
                }));
        }

        var names = undeclared
            .SelectMany(pair => pair.Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        var lines = undeclared
            .OrderBy(pair => pair.Key.ValueType.Name, StringComparer.Ordinal)
            .Select(pair =>
                $"  {pair.Key.ValueType.Name} ({pair.Key.ScopeDescription}) is not declared by: "
                + string.Join(", ", pair.Value));

        var count = names.Count;
        var header = count == 1
            ? "One registered message states no position on a reported declaration:"
            : $"{count} registered messages state no position on a reported declaration:";

        return Task.FromResult(new DiagnosticResult(
            DiagnosticStatus.Degraded,
            header
            + Environment.NewLine + string.Join(Environment.NewLine, lines)
            + Environment.NewLine
            + "Declare the value with an attribute or a definition class, or record why the message does not need it "
            + "with [DeclarationExempt(typeof(TValue), \"rationale\")]. Narrow the requirement instead if the "
            + "messages listed were never meant to be in its scope.",
            new Dictionary<string, object>
            {
                ["component"] = "declarations",
                ["undeclaredCount"] = count,
                ["undeclared"] = names.ToArray()
            }));
    }

    /// <summary>
    ///     Resolves a service from a dispatch scope, falling back to the provider the probe was given.
    /// </summary>
    /// <typeparam name="TService">The service type to resolve.</typeparam>
    /// <returns>The registered service, or <see langword="null" /> when none is registered.</returns>
    /// <remarks>
    ///     Resolving from a dispatch scope keeps the probe consistent with the other probes under a container that
    ///     validates scopes, even though both services looked up here are singletons.
    /// </remarks>
    private TService? Resolve<TService>()
        where TService : class
    {
        if (_serviceProvider.GetService(typeof(IMessageDispatchScopeFactory)) is not IMessageDispatchScopeFactory factory)
        {
            return _serviceProvider.GetService(typeof(TService)) as TService;
        }

        using var scope = factory.CreateScope();
        return scope.ServiceProvider.GetService(typeof(TService)) as TService;
    }
}
