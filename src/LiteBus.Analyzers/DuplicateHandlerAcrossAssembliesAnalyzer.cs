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
        [DiagnosticDescriptors.DuplicateHandlerAcrossAssemblies];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(AnalyzeCompilationStart);
    }

    /// <summary>
    ///     Gathers handler registrations for one compilation.
    /// </summary>
    /// <param name="context">The compilation start analysis context.</param>
    private static void AnalyzeCompilationStart(CompilationStartAnalysisContext context)
    {
        var typeCache = new LiteBusTypeCache(context.Compilation);

        if (!HandlerAnalysis.AnyHandlerInterfaceResolves(typeCache))
        {
            return;
        }

        var collector = new HandlerRegistrationCollector(typeCache);

        context.RegisterSymbolAction(
            symbolContext => AnalyzeSymbol(symbolContext, collector),
            SymbolKind.NamedType);

        context.RegisterCompilationEndAction(
            endContext => AnalyzeCompilationEnd(endContext, collector));
    }

    /// <summary>
    ///     Adds the handler registrations declared on one type symbol.
    /// </summary>
    /// <param name="context">The symbol analysis context.</param>
    /// <param name="collector">The handler registration collector.</param>
    private static void AnalyzeSymbol(SymbolAnalysisContext context, HandlerRegistrationCollector collector)
    {
        if (context.Symbol is INamedTypeSymbol symbol)
        {
            collector.Add(symbol);
        }
    }

    /// <summary>
    ///     Reports handler names duplicated across assemblies.
    /// </summary>
    /// <param name="context">The compilation analysis context.</param>
    /// <param name="collector">The handler registration collector.</param>
    private static void AnalyzeCompilationEnd(CompilationAnalysisContext context, HandlerRegistrationCollector collector)
    {
        var handlers = collector.ToRegistrations(context.CancellationToken);

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
