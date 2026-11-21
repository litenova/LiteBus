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
///     Reports durable message types that carry no <c>[MessageContract]</c> in a compilation whose assembly
///     declares <c>[assembly: MessageContractsRegisteredIn]</c>.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UnattributedContractAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        [DiagnosticDescriptors.UnattributedContractInRegisteredAssembly];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(AnalyzeCompilationStart);
    }

    /// <summary>
    ///     Gathers declared message types for one compilation that delegates contract registration.
    /// </summary>
    /// <param name="context">The compilation start analysis context.</param>
    private static void AnalyzeCompilationStart(CompilationStartAnalysisContext context)
    {
        if (!ContractRegistrationAnalysis.AnyDurableMarkerResolves(context.Compilation) ||
            !ContractRegistrationAnalysis.CarriesRegisteredInAttribute(context.Compilation.Assembly))
        {
            return;
        }

        var candidates = new ConcurrentBag<INamedTypeSymbol>();

        context.RegisterSyntaxNodeAction(
            nodeContext => AnalyzeTypeDeclaration(nodeContext, candidates),
            SyntaxKind.ClassDeclaration,
            SyntaxKind.StructDeclaration,
            SyntaxKind.RecordDeclaration);

        context.RegisterCompilationEndAction(
            endContext => AnalyzeCompilationEnd(endContext, candidates));
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
    ///     Reports declared durable message types that carry no <c>[MessageContract]</c>.
    /// </summary>
    /// <param name="context">The compilation analysis context.</param>
    /// <param name="candidates">The declared types, one entry per declaration node.</param>
    /// <remarks>
    ///     Consuming compilations trust the named assembly for every type declared here, and the named compilation
    ///     checks only attributed types, so an unattributed durable message type would never be checked anywhere.
    /// </remarks>
    private static void AnalyzeCompilationEnd(
        CompilationAnalysisContext context,
        ConcurrentBag<INamedTypeSymbol> candidates)
    {
        if (candidates.IsEmpty)
        {
            return;
        }

        var registeringAssemblies = string.Join(
            ", ",
            ContractRegistrationAnalysis.GetRegisteredInAssemblyNames(context.Compilation.Assembly)
                .OrderBy(name => name, StringComparer.Ordinal));

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
                ContractRegistrationAnalysis.HasMessageContractAttribute(symbol) ||
                !ContractRegistrationAnalysis.IsDurableMessageType(symbol, context.Compilation))
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.UnattributedContractInRegisteredAssembly,
                location,
                ContractRegistrationAnalysis.GetClosedRegistrationTypeDisplay(symbol),
                registeringAssemblies));
        }
    }
}
