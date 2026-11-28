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
            endContext => AnalyzeCompilationEnd(endContext, typeCache, handlers, commands));
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
    /// <param name="typeCache">The per-compilation type cache.</param>
    /// <param name="handlers">The handler registration collector.</param>
    /// <param name="commands">The command type collector.</param>
    /// <remarks>
    ///     Only commands declared in the analyzed assembly are candidates, while handlers from eligible referenced
    ///     assemblies still satisfy coverage. A command whose assembly carries
    ///     <c>[assembly: MessageContractsRegisteredIn("X")]</c> for another assembly is skipped here; the named
    ///     compilation reports the referenced commands it does not handle at its first location. A handler that lives
    ///     in a dependent project is compiled separately and stays invisible here; co-locate the handler with the
    ///     command or suppress the diagnostic.
    /// </remarks>
    private static void AnalyzeCompilationEnd(
        CompilationAnalysisContext context,
        LiteBusTypeCache typeCache,
        HandlerRegistrationCollector handlers,
        MessageTypeCollector commands)
    {
        var mainHandlers = handlers.ToRegistrations(context.CancellationToken)
            .Where(handler => handler.Pipeline == "command")
            .ToImmutableArray();

        var openGenericHandlers = commands.ToOpenGenericMainHandlers(context.CancellationToken);

        foreach (var command in commands.ToMessageTypes())
        {
            if (ContractRegistrationAnalysis.IsRegisteredInAnotherAssembly(command.MessageType, context.Compilation))
            {
                continue;
            }

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

        ReportMissingReferencedHandlers(context, typeCache, mainHandlers, openGenericHandlers);
    }

    /// <summary>
    ///     Reports referenced command types that name the analyzed compilation and lack a main handler.
    /// </summary>
    /// <param name="context">The compilation analysis context.</param>
    /// <param name="typeCache">The per-compilation type cache.</param>
    /// <param name="mainHandlers">The discovered main handler registrations.</param>
    /// <param name="openGenericHandlers">The discovered open generic main handler type definitions.</param>
    /// <remarks>
    ///     A command declared in a referenced assembly has no source location in the analyzed compilation, so the
    ///     diagnostic is reported at the first location of the analyzed compilation.
    /// </remarks>
    private static void ReportMissingReferencedHandlers(
        CompilationAnalysisContext context,
        LiteBusTypeCache typeCache,
        ImmutableArray<HandlerRegistration> mainHandlers,
        ImmutableArray<INamedTypeSymbol> openGenericHandlers)
    {
        var unhandled = HandlerCoverageAnalysis.GetUnhandledReferencedMessages(
            context.Compilation,
            typeCache,
            MessageKind.Command,
            mainHandlers,
            openGenericHandlers,
            context.CancellationToken);

        if (unhandled.IsEmpty)
        {
            return;
        }

        var location = HandlerCoverageAnalysis.GetFirstCompilationLocation(context.Compilation);

        foreach (var command in unhandled)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.MissingCommandHandler,
                location,
                HandlerAnalysis.GetMessageTypeDisplay(command)));
        }
    }
}
