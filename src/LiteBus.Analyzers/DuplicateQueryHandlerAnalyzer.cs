using System.Collections.Immutable;
using System.Linq;
using LiteBus.Analyzers.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace LiteBus.Analyzers;

/// <summary>
///     Reports duplicate query and stream query handlers for the same query type.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DuplicateQueryHandlerAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        [DiagnosticDescriptors.DuplicateQueryHandler];

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

        if (!HandlerAnalysis.AnyPipelineInterfaceResolves(typeCache, "query") &&
            !HandlerAnalysis.AnyPipelineInterfaceResolves(typeCache, "stream query"))
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
    ///     Reports duplicate query and stream query handlers within a compilation.
    /// </summary>
    /// <param name="context">The compilation analysis context.</param>
    /// <param name="collector">The handler registration collector.</param>
    private static void AnalyzeCompilationEnd(CompilationAnalysisContext context, HandlerRegistrationCollector collector)
    {
        var registrations = collector.ToRegistrations(context.CancellationToken);

        ReportDuplicateHandlers(context, registrations, "query");
        ReportDuplicateHandlers(context, registrations, "stream query");
    }

    /// <summary>
    ///     Reports duplicate handlers for one query pipeline stage.
    /// </summary>
    /// <param name="context">The compilation analysis context.</param>
    /// <param name="registrations">The discovered handler registrations.</param>
    /// <param name="pipeline">The handler pipeline stage name.</param>
    private static void ReportDuplicateHandlers(
        CompilationAnalysisContext context,
        ImmutableArray<HandlerRegistration> registrations,
        string pipeline)
    {
        var groups = registrations
            .Where(handler => handler.Pipeline == pipeline)
            .GroupBy(handler => handler.MessageType, SymbolEqualityComparer.Default);

        foreach (var group in groups)
        {
            var matches = group.ToList();

            if (matches.Count < 2)
            {
                continue;
            }

            var first = matches[0];
            var second = matches[1];
            var messageDisplay = HandlerAnalysis.GetMessageTypeDisplay(first.MessageType);

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.DuplicateQueryHandler,
                second.Location,
                messageDisplay,
                first.HandlerType.Name,
                second.HandlerType.Name));
        }
    }
}
