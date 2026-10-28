using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using LiteBus.Analyzers.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace LiteBus.Analyzers;

/// <summary>
///     Reports durable message types that declare <c>[MessageContract]</c> without explicit registration.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ExplicitMessageContractRegistrationAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        [DiagnosticDescriptors.ExplicitMessageContractRegistration];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(AnalyzeCompilationStart);
    }

    /// <summary>
    ///     Gathers declared message types and contract registrations for one compilation.
    /// </summary>
    /// <param name="context">The compilation start analysis context.</param>
    private static void AnalyzeCompilationStart(CompilationStartAnalysisContext context)
    {
        if (!ContractRegistrationAnalysis.AnyDurableMarkerResolves(context.Compilation))
        {
            return;
        }

        var contracts = new ContractRegistrationCollector(context.Compilation);
        var candidates = new ConcurrentBag<INamedTypeSymbol>();

        context.RegisterSyntaxNodeAction(
            nodeContext => AnalyzeInvocation(nodeContext, contracts),
            SyntaxKind.InvocationExpression);

        context.RegisterSyntaxNodeAction(
            nodeContext => AnalyzeTypeDeclaration(nodeContext, candidates),
            SyntaxKind.ClassDeclaration,
            SyntaxKind.StructDeclaration,
            SyntaxKind.RecordDeclaration);

        context.RegisterCompilationEndAction(
            endContext => AnalyzeCompilationEnd(endContext, contracts, candidates));
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
    ///     Records the type declared by one type declaration node.
    /// </summary>
    /// <param name="context">The syntax node analysis context.</param>
    /// <param name="candidates">The declared types, one entry per declaration node.</param>
    /// <remarks>
    ///     One entry is recorded per declaration node; the compilation-end action deduplicates by symbol so a partial
    ///     type with two declarations reports once, at its first location.
    /// </remarks>
    private static void AnalyzeTypeDeclaration(SyntaxNodeAnalysisContext context, ConcurrentBag<INamedTypeSymbol> candidates)
    {
        if (context.SemanticModel.GetDeclaredSymbol(
                (TypeDeclarationSyntax) context.Node,
                context.CancellationToken) is not INamedTypeSymbol symbol)
        {
            return;
        }

        candidates.Add(symbol);
    }

    /// <summary>
    ///     Reports declared durable message types without explicit contract registration.
    /// </summary>
    /// <param name="context">The compilation analysis context.</param>
    /// <param name="contracts">The contract registration collector.</param>
    /// <param name="candidates">The declared types, one entry per declaration node.</param>
    private static void AnalyzeCompilationEnd(
        CompilationAnalysisContext context,
        ContractRegistrationCollector contracts,
        ConcurrentBag<INamedTypeSymbol> candidates)
    {
        if (candidates.IsEmpty)
        {
            return;
        }

        var registeredTypes = contracts.ToRegisteredTypes();
        var registeredAssemblies = contracts.ToRegisteredAssemblies();
        var order = CompilationEndOrdering.GetSyntaxTreeOrder(context.Compilation);

        var ordered = candidates
            .Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default)
            .Select(symbol => (Symbol: symbol, Location: symbol.Locations.FirstOrDefault() ?? Location.None))
            .OrderBy(entry => CompilationEndOrdering.GetOrderKey(entry.Location, order)?.TreeIndex ?? int.MaxValue)
            .ThenBy(entry => CompilationEndOrdering.GetOrderKey(entry.Location, order)?.SpanStart ?? int.MaxValue)
            .ThenBy(entry => LiteBusSymbols.GetMetadataName(entry.Symbol), StringComparer.Ordinal)
            .ToList();

        foreach (var (symbol, location) in ordered)
        {
            if (symbol.TypeKind == TypeKind.TypeParameter ||
                HandlerAnalysis.IsGenericTypeDefinition(symbol) ||
                !ContractRegistrationAnalysis.HasMessageContractAttribute(symbol) ||
                !ContractRegistrationAnalysis.IsDurableMessageType(symbol, context.Compilation))
            {
                continue;
            }

            if (ContractRegistrationAnalysis.IsExplicitlyRegistered(symbol, registeredTypes, registeredAssemblies))
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.ExplicitMessageContractRegistration,
                location,
                ContractRegistrationAnalysis.GetClosedRegistrationTypeDisplay(symbol)));
        }
    }
}
