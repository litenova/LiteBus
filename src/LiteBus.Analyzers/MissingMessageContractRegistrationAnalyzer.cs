using System.Collections.Immutable;
using System.Linq;
using LiteBus.Analyzers.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace LiteBus.Analyzers;

/// <summary>
///     Reports handled message types that lack durable contract registration.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MissingMessageContractRegistrationAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        [DiagnosticDescriptors.MissingMessageContractRegistration];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(AnalyzeCompilationStart);
    }

    /// <summary>
    ///     Gathers handler registrations and contract registrations for one compilation.
    /// </summary>
    /// <param name="context">The compilation start analysis context.</param>
    private static void AnalyzeCompilationStart(CompilationStartAnalysisContext context)
    {
        var typeCache = new LiteBusTypeCache(context.Compilation);

        if (!HandlerAnalysis.AnyPipelineInterfaceResolves(typeCache, "command") &&
            !HandlerAnalysis.AnyPipelineInterfaceResolves(typeCache, "event"))
        {
            return;
        }

        var handlers = new HandlerRegistrationCollector(typeCache);
        var contracts = new ContractRegistrationCollector(context.Compilation);

        context.RegisterSymbolAction(
            symbolContext => AnalyzeSymbol(symbolContext, handlers),
            SymbolKind.NamedType);

        context.RegisterSyntaxNodeAction(
            nodeContext => AnalyzeInvocation(nodeContext, contracts),
            SyntaxKind.InvocationExpression);

        context.RegisterCompilationEndAction(
            endContext => AnalyzeCompilationEnd(endContext, handlers, contracts));
    }

    /// <summary>
    ///     Adds the handler registrations declared on one type symbol.
    /// </summary>
    /// <param name="context">The symbol analysis context.</param>
    /// <param name="handlers">The handler registration collector.</param>
    private static void AnalyzeSymbol(SymbolAnalysisContext context, HandlerRegistrationCollector handlers)
    {
        if (context.Symbol is INamedTypeSymbol symbol)
        {
            handlers.Add(symbol);
        }
    }

    /// <summary>
    ///     Adds the contract registration performed by one invocation node.
    /// </summary>
    /// <param name="context">The syntax node analysis context.</param>
    /// <param name="contracts">The contract registration collector.</param>
    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context, ContractRegistrationCollector contracts)
    {
        contracts.AddInvocation(
            context.SemanticModel,
            (InvocationExpressionSyntax) context.Node,
            context.CancellationToken);
    }

    /// <summary>
    ///     Reports handled message types without durable contract registration.
    /// </summary>
    /// <param name="context">The compilation analysis context.</param>
    /// <param name="handlers">The handler registration collector.</param>
    /// <param name="contracts">The contract registration collector.</param>
    private static void AnalyzeCompilationEnd(
        CompilationAnalysisContext context,
        HandlerRegistrationCollector handlers,
        ContractRegistrationCollector contracts)
    {
        var registeredContracts = contracts.ToRegisteredTypes();
        var registeredAssemblies = contracts.ToRegisteredAssemblies();

        var matches = handlers.ToRegistrations(context.CancellationToken)
            .Where(handler => handler.Pipeline is "command" or "event")
            .ToList();

        foreach (var handler in matches)
        {
            if (handler.MessageType.TypeKind == TypeKind.TypeParameter ||
                HandlerAnalysis.IsGenericTypeDefinition(handler.MessageType))
            {
                continue;
            }

            if (ContractRegistrationAnalysis.HasMessageContractAttribute(handler.MessageType) ||
                ContractRegistrationAnalysis.IsExplicitlyRegistered(
                    handler.MessageType,
                    registeredContracts,
                    registeredAssemblies))
            {
                continue;
            }

            var closedTypeDisplay = ContractRegistrationAnalysis.GetClosedRegistrationTypeDisplay(handler.MessageType);

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.MissingMessageContractRegistration,
                handler.Location,
                HandlerAnalysis.GetMessageTypeDisplay(handler.MessageType),
                handler.HandlerType.Name,
                closedTypeDisplay));
        }
    }
}
