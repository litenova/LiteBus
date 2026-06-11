using System;
using System.Collections.Immutable;
using System.Linq;
using LiteBus.Analyzers.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace LiteBus.Analyzers;

/// <summary>
///     Reports handler type names that appear in multiple assemblies and may be registered twice.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DuplicateHandlerAcrossAssembliesAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(DiagnosticDescriptors.DuplicateHandlerAcrossAssemblies);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationAction(AnalyzeCompilation);
    }

    /// <summary>
    ///     Reports handler names duplicated across assemblies.
    /// </summary>
    /// <param name="context">The compilation analysis context.</param>
    private static void AnalyzeCompilation(CompilationAnalysisContext context)
    {
        var handlers = HandlerAnalysis.CollectHandlerRegistrations(context.Compilation, context.CancellationToken);

        foreach (var group in handlers
                     .GroupBy(handler => handler.HandlerType.Name)
                     .Where(group => group.Select(item => item.HandlerType.ContainingAssembly.Name).Distinct().Count() > 1))
        {
            var assemblies = group
                .Select(item => item.HandlerType.ContainingAssembly.Name)
                .Distinct()
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            var location = group.First().Location;

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.DuplicateHandlerAcrossAssemblies,
                location,
                group.Key,
                assemblies[0],
                assemblies[1]));
        }
    }
}