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
    /// <remarks>
    ///     A declared type whose assembly carries <c>[assembly: MessageContractsRegisteredIn("X")]</c> is trusted
    ///     when the analyzed compilation is not <c>X</c>. When the analyzed compilation is <c>X</c>, every
    ///     <c>[MessageContract]</c> type of a referenced assembly that names <c>X</c> and lacks registration is
    ///     reported without a source location.
    /// </remarks>
    private static void AnalyzeCompilationEnd(
        CompilationAnalysisContext context,
        ContractRegistrationCollector contracts,
        ConcurrentBag<INamedTypeSymbol> candidates)
    {
        var registeredTypes = contracts.ToRegisteredTypes();
        var registeredAssemblies = contracts.ToRegisteredAssemblies();

        if (!candidates.IsEmpty)
        {
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

                if (ContractRegistrationAnalysis.IsRegisteredInAnotherAssembly(symbol, context.Compilation))
                {
                    continue;
                }

                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.ExplicitMessageContractRegistration,
                    location,
                    ContractRegistrationAnalysis.GetClosedRegistrationTypeDisplay(symbol)));
            }
        }

        ReportUnregisteredReferencedContracts(context, registeredTypes, registeredAssemblies);
    }

    /// <summary>
    ///     Reports referenced contract types that name the analyzed compilation but lack registration.
    /// </summary>
    /// <param name="context">The compilation analysis context.</param>
    /// <param name="registeredTypes">Types registered through <c>Register</c>.</param>
    /// <param name="registeredAssemblies">Assemblies registered through <c>RegisterFromAssembly</c>.</param>
    private static void ReportUnregisteredReferencedContracts(
        CompilationAnalysisContext context,
        ImmutableHashSet<ITypeSymbol> registeredTypes,
        ImmutableHashSet<IAssemblySymbol> registeredAssemblies)
    {
        var unregistered = ContractRegistrationAnalysis.GetUnregisteredReferencedContracts(
            context.Compilation,
            registeredTypes,
            registeredAssemblies);

        foreach (var symbol in unregistered)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.ExplicitMessageContractRegistration,
                Location.None,
                ContractRegistrationAnalysis.GetClosedRegistrationTypeDisplay(symbol)));
        }
    }
}
