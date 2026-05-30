using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using LiteBus.Analyzers.Analysis;

namespace LiteBus.Analyzers;

/// <summary>
///     Reports duplicate event handlers with overlapping routing for the same event type.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DuplicateEventHandlerAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(DiagnosticDescriptors.DuplicateEventHandler);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationAction(AnalyzeCompilation);
    }

    /// <summary>
    ///     Reports duplicate event handler routing within a compilation.
    /// </summary>
    /// <param name="context">The compilation analysis context.</param>
    private static void AnalyzeCompilation(CompilationAnalysisContext context)
    {
        var eventHandlers = HandlerAnalysis.CollectHandlerRegistrations(context.Compilation, context.CancellationToken)
            .Where(handler => handler.Pipeline == "event")
            .ToList();

        for (var leftIndex = 0; leftIndex < eventHandlers.Count; leftIndex++)
        {
            for (var rightIndex = leftIndex + 1; rightIndex < eventHandlers.Count; rightIndex++)
            {
                var left = eventHandlers[leftIndex];
                var right = eventHandlers[rightIndex];

                if (!SymbolEqualityComparer.Default.Equals(left.MessageType, right.MessageType))
                {
                    continue;
                }

                if (!HandlerAnalysis.TagsOverlap(left.Tags, right.Tags))
                {
                    continue;
                }

                var messageDisplay = HandlerAnalysis.GetMessageTypeDisplay(left.MessageType);

                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.DuplicateEventHandler,
                    right.Location,
                    messageDisplay,
                    left.HandlerType.Name,
                    right.HandlerType.Name));
            }
        }
    }
}
