using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using LiteBus.Analyzers.Analysis;

namespace LiteBus.Analyzers;

/// <summary>
///     Reports duplicate query handlers for the same query type.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DuplicateQueryHandlerAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(DiagnosticDescriptors.DuplicateQueryHandler);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationAction(AnalyzeCompilation);
    }

    /// <summary>
    ///     Reports duplicate query handlers within a compilation.
    /// </summary>
    /// <param name="context">The compilation analysis context.</param>
    private static void AnalyzeCompilation(CompilationAnalysisContext context)
    {
        var groups = HandlerAnalysis.CollectHandlerRegistrations(context.Compilation, context.CancellationToken)
            .Where(handler => handler.Pipeline == "query")
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
                DiagnosticDescriptors.DuplicateQueryHandler,
                second.Location,
                messageDisplay,
                first.HandlerType.Name,
                second.HandlerType.Name));
        }
    }
}
