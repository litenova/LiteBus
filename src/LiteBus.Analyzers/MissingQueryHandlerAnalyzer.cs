using System.Collections.Immutable;
using System.Linq;
using LiteBus.Analyzers.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace LiteBus.Analyzers;

/// <summary>
///     Reports query and stream query types that have no main handler in the compilation.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MissingQueryHandlerAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        [DiagnosticDescriptors.MissingQueryHandler];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationAction(AnalyzeCompilation);
    }

    /// <summary>
    ///     Reports query and stream query types without a main handler within a compilation.
    /// </summary>
    /// <param name="context">The compilation analysis context.</param>
    private static void AnalyzeCompilation(CompilationAnalysisContext context)
    {
        ReportMissingHandlers(
            context,
            MessageKind.Query,
            "query");

        ReportMissingHandlers(
            context,
            MessageKind.StreamQuery,
            "stream query");
    }

    /// <summary>
    ///     Reports message types of the supplied kind that lack a main handler.
    /// </summary>
    /// <param name="context">The compilation analysis context.</param>
    /// <param name="kind">The message kind to inspect.</param>
    /// <param name="pipeline">The handler pipeline stage name.</param>
    private static void ReportMissingHandlers(
        CompilationAnalysisContext context,
        MessageKind kind,
        string pipeline)
    {
        var handlers = HandlerAnalysis.CollectHandlerRegistrations(context.Compilation, context.CancellationToken)
            .Where(handler => handler.Pipeline == pipeline)
            .ToImmutableArray();

        var openGenericHandlers = MessageAnalysis.CollectOpenGenericMainHandlers(
            context.Compilation,
            kind,
            context.CancellationToken);

        var queries = MessageAnalysis.CollectMessageTypes(
            context.Compilation,
            kind,
            context.CancellationToken);

        foreach (var query in queries)
        {
            if (MessageAnalysis.HasMainHandler(
                    query.MessageType,
                    handlers,
                    openGenericHandlers,
                    context.Compilation))
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.MissingQueryHandler,
                LiteBusSymbols.GetDiagnosticLocation(context.Compilation, query.Location),
                HandlerAnalysis.GetMessageTypeDisplay(query.MessageType)));
        }
    }
}
