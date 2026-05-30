using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using LiteBus.Analyzers.Analysis;

namespace LiteBus.Analyzers;

/// <summary>
///     Reports handlers that share the same message, pipeline stage, and priority value.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HandlerPriorityConflictAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(DiagnosticDescriptors.HandlerPriorityConflict);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationAction(AnalyzeCompilation);
    }

    /// <summary>
    ///     Reports handler priority conflicts within a compilation.
    /// </summary>
    /// <param name="context">The compilation analysis context.</param>
    private static void AnalyzeCompilation(CompilationAnalysisContext context)
    {
        var handlers = HandlerAnalysis.CollectHandlerRegistrations(context.Compilation, context.CancellationToken);

        for (var leftIndex = 0; leftIndex < handlers.Length; leftIndex++)
        {
            for (var rightIndex = leftIndex + 1; rightIndex < handlers.Length; rightIndex++)
            {
                var left = handlers[leftIndex];
                var right = handlers[rightIndex];

                if (!SymbolEqualityComparer.Default.Equals(left.MessageType, right.MessageType) ||
                    left.Pipeline != right.Pipeline ||
                    left.Priority != right.Priority)
                {
                    continue;
                }

                var messageDisplay = HandlerAnalysis.GetMessageTypeDisplay(left.MessageType);

                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.HandlerPriorityConflict,
                    right.Location,
                    messageDisplay,
                    left.Pipeline,
                    left.Priority,
                    left.HandlerType.Name,
                    right.HandlerType.Name));
            }
        }
    }
}
