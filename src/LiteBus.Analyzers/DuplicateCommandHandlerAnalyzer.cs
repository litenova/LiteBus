using System.Collections.Immutable;
using System.Linq;
using LiteBus.Analyzers.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace LiteBus.Analyzers;

/// <summary>
///     Reports duplicate command handlers for the same command type.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DuplicateCommandHandlerAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        [DiagnosticDescriptors.DuplicateCommandHandler];

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

        if (!HandlerAnalysis.AnyPipelineInterfaceResolves(typeCache, "command"))
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
    ///     Reports duplicate command handlers within a compilation.
    /// </summary>
    /// <param name="context">The compilation analysis context.</param>
    /// <param name="collector">The handler registration collector.</param>
    private static void AnalyzeCompilationEnd(CompilationAnalysisContext context, HandlerRegistrationCollector collector)
    {
        var groups = collector.ToRegistrations(context.CancellationToken)
            .Where(handler => handler.Pipeline == "command")
            .GroupBy(handler => handler.MessageType, SymbolEqualityComparer.Default);

        foreach (var group in groups)
        {
            var registrations = group.ToList();

            if (registrations.Count < 2)
            {
                continue;
            }

            var first = registrations[0];
            var second = registrations[1];
            var messageDisplay = HandlerAnalysis.GetMessageTypeDisplay(first.MessageType);

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.DuplicateCommandHandler,
                second.Location,
                messageDisplay,
                first.HandlerType.Name,
                second.HandlerType.Name));
        }
    }
}
