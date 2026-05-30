using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using LiteBus.Analyzers.Analysis;

namespace LiteBus.Analyzers;

/// <summary>
///     Reports command types that have no main command handler in the compilation.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MissingCommandHandlerAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(DiagnosticDescriptors.MissingCommandHandler);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationAction(AnalyzeCompilation);
    }

    /// <summary>
    ///     Reports command types without a main handler within a compilation.
    /// </summary>
    /// <param name="context">The compilation analysis context.</param>
    private static void AnalyzeCompilation(CompilationAnalysisContext context)
    {
        var handlers = HandlerAnalysis.CollectHandlerRegistrations(context.Compilation, context.CancellationToken)
            .Where(handler => handler.Pipeline == "command")
            .ToImmutableArray();
        var openGenericHandlers = MessageAnalysis.CollectOpenGenericMainHandlers(
            context.Compilation,
            MessageKind.Command,
            context.CancellationToken);
        var commands = MessageAnalysis.CollectMessageTypes(
            context.Compilation,
            MessageKind.Command,
            context.CancellationToken);

        foreach (var command in commands)
        {
            if (MessageAnalysis.HasMainHandler(
                    command.MessageType,
                    handlers,
                    openGenericHandlers,
                    context.Compilation))
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.MissingCommandHandler,
                command.Location,
                HandlerAnalysis.GetMessageTypeDisplay(command.MessageType)));
        }
    }
}
