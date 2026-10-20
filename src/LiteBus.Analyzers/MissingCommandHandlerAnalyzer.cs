using System.Collections.Immutable;
using System.Linq;
using LiteBus.Analyzers.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace LiteBus.Analyzers;

/// <summary>
///     Reports command types that have no main command handler in the compilation.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MissingCommandHandlerAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        [DiagnosticDescriptors.MissingCommandHandler];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(AnalyzeCompilationStart);
    }

    /// <summary>
    ///     Gathers handler registrations and command types for one compilation.
    /// </summary>
    /// <param name="context">The compilation start analysis context.</param>
    private static void AnalyzeCompilationStart(CompilationStartAnalysisContext context)
    {
        var typeCache = new LiteBusTypeCache(context.Compilation);

        if (!MessageAnalysis.AnyMarkerInterfaceResolves(typeCache, MessageKind.Command))
        {
            return;
        }

        var handlers = new HandlerRegistrationCollector(typeCache);
        var commands = new MessageTypeCollector(typeCache, MessageKind.Command);

        context.RegisterSymbolAction(
            symbolContext => AnalyzeSymbol(symbolContext, handlers, commands),
            SymbolKind.NamedType);

        context.RegisterCompilationEndAction(
            endContext => AnalyzeCompilationEnd(endContext, handlers, commands));
    }

    /// <summary>
    ///     Adds the handler and command facts declared on one type symbol.
    /// </summary>
    /// <param name="context">The symbol analysis context.</param>
    /// <param name="handlers">The handler registration collector.</param>
    /// <param name="commands">The command type collector.</param>
    private static void AnalyzeSymbol(
        SymbolAnalysisContext context,
        HandlerRegistrationCollector handlers,
        MessageTypeCollector commands)
    {
        if (context.Symbol is INamedTypeSymbol symbol)
        {
            handlers.Add(symbol);
            commands.Add(symbol);
        }
    }

    /// <summary>
    ///     Reports command types without a main handler within a compilation.
    /// </summary>
    /// <param name="context">The compilation analysis context.</param>
    /// <param name="handlers">The handler registration collector.</param>
    /// <param name="commands">The command type collector.</param>
    private static void AnalyzeCompilationEnd(
        CompilationAnalysisContext context,
        HandlerRegistrationCollector handlers,
        MessageTypeCollector commands)
    {
        var mainHandlers = handlers.ToRegistrations(context.CancellationToken)
            .Where(handler => handler.Pipeline == "command")
            .ToImmutableArray();

        var openGenericHandlers = commands.ToOpenGenericMainHandlers(context.CancellationToken);

        foreach (var command in commands.ToMessageTypes(context.CancellationToken))
        {
            if (MessageAnalysis.HasMainHandler(
                    command.MessageType,
                    mainHandlers,
                    openGenericHandlers,
                    context.Compilation))
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.MissingCommandHandler,
                LiteBusSymbols.GetDiagnosticLocation(context.Compilation, command.Location),
                HandlerAnalysis.GetMessageTypeDisplay(command.MessageType)));
        }
    }
}
