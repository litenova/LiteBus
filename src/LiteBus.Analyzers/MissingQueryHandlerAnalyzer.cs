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
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(AnalyzeCompilationStart);
    }

    /// <summary>
    ///     Gathers handler registrations and query types for one compilation.
    /// </summary>
    /// <param name="context">The compilation start analysis context.</param>
    private static void AnalyzeCompilationStart(CompilationStartAnalysisContext context)
    {
        var typeCache = new LiteBusTypeCache(context.Compilation);

        if (!MessageAnalysis.AnyMarkerInterfaceResolves(typeCache, MessageKind.Query) &&
            !MessageAnalysis.AnyMarkerInterfaceResolves(typeCache, MessageKind.StreamQuery))
        {
            return;
        }

        var handlers = new HandlerRegistrationCollector(typeCache);
        var queries = new MessageTypeCollector(typeCache, MessageKind.Query);
        var streamQueries = new MessageTypeCollector(typeCache, MessageKind.StreamQuery);

        context.RegisterSymbolAction(
            symbolContext => AnalyzeSymbol(symbolContext, handlers, queries, streamQueries),
            SymbolKind.NamedType);

        context.RegisterCompilationEndAction(
            endContext => AnalyzeCompilationEnd(endContext, handlers, queries, streamQueries));
    }

    /// <summary>
    ///     Adds the handler and query facts declared on one type symbol.
    /// </summary>
    /// <param name="context">The symbol analysis context.</param>
    /// <param name="handlers">The handler registration collector.</param>
    /// <param name="queries">The query type collector.</param>
    /// <param name="streamQueries">The stream query type collector.</param>
    private static void AnalyzeSymbol(
        SymbolAnalysisContext context,
        HandlerRegistrationCollector handlers,
        MessageTypeCollector queries,
        MessageTypeCollector streamQueries)
    {
        if (context.Symbol is INamedTypeSymbol symbol)
        {
            handlers.Add(symbol);
            queries.Add(symbol);
            streamQueries.Add(symbol);
        }
    }

    /// <summary>
    ///     Reports query and stream query types without a main handler within a compilation.
    /// </summary>
    /// <param name="context">The compilation analysis context.</param>
    /// <param name="handlers">The handler registration collector.</param>
    /// <param name="queries">The query type collector.</param>
    /// <param name="streamQueries">The stream query type collector.</param>
    private static void AnalyzeCompilationEnd(
        CompilationAnalysisContext context,
        HandlerRegistrationCollector handlers,
        MessageTypeCollector queries,
        MessageTypeCollector streamQueries)
    {
        var registrations = handlers.ToRegistrations(context.CancellationToken);

        ReportMissingHandlers(context, registrations, queries, "query");
        ReportMissingHandlers(context, registrations, streamQueries, "stream query");
    }

    /// <summary>
    ///     Reports message types of the supplied kind that lack a main handler.
    /// </summary>
    /// <param name="context">The compilation analysis context.</param>
    /// <param name="registrations">The discovered handler registrations.</param>
    /// <param name="messages">The message type collector for the kind.</param>
    /// <param name="pipeline">The handler pipeline stage name.</param>
    /// <remarks>
    ///     Only messages declared in the analyzed assembly are candidates, while handlers from eligible referenced
    ///     assemblies still satisfy coverage. A handler that lives in a dependent project is compiled separately and
    ///     stays invisible here; co-locate the handler with the message or suppress the diagnostic.
    /// </remarks>
    private static void ReportMissingHandlers(
        CompilationAnalysisContext context,
        ImmutableArray<HandlerRegistration> registrations,
        MessageTypeCollector messages,
        string pipeline)
    {
        var mainHandlers = registrations
            .Where(handler => handler.Pipeline == pipeline)
            .ToImmutableArray();

        var openGenericHandlers = messages.ToOpenGenericMainHandlers(context.CancellationToken);

        foreach (var query in messages.ToMessageTypes())
        {
            if (MessageAnalysis.HasMainHandler(
                    query.MessageType,
                    mainHandlers,
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
