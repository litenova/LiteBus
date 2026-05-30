using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using LiteBus.Analyzers.Analysis;

namespace LiteBus.Analyzers;

/// <summary>
///     Reports query handlers that depend on side-effecting mediators or durable writers.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class QueryHandlerImpurityAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(DiagnosticDescriptors.QueryHandlerImpurity);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
    }

    /// <summary>
    ///     Reports impure dependencies declared on query handlers.
    /// </summary>
    /// <param name="context">The symbol analysis context.</param>
    private static void AnalyzeNamedType(SymbolAnalysisContext context)
    {
        if (context.Symbol is not INamedTypeSymbol handlerType)
        {
            return;
        }

        if (!LiteBusSymbols.ImplementsGenericInterface(
                handlerType,
                context.Compilation,
                "LiteBus.Queries.Abstractions.IQueryHandler`2"))
        {
            return;
        }

        foreach (var member in handlerType.GetMembers())
        {
            if (member is IMethodSymbol { MethodKind: MethodKind.Constructor } constructor)
            {
                foreach (var parameter in constructor.Parameters)
                {
                    ReportIfImpure(context, handlerType, parameter.Type, parameter.Locations.FirstOrDefault());
                }
            }
        }
    }

    /// <summary>
    ///     Reports a diagnostic when the dependency type is impure.
    /// </summary>
    /// <param name="context">The symbol analysis context.</param>
    /// <param name="handlerType">The query handler type symbol.</param>
    /// <param name="dependencyType">The dependency type symbol.</param>
    /// <param name="location">The diagnostic location.</param>
    private static void ReportIfImpure(
        SymbolAnalysisContext context,
        INamedTypeSymbol handlerType,
        ITypeSymbol dependencyType,
        Location? location)
    {
        var dependencyMetadataName = GetImpureDependencyMetadataName(dependencyType, context.Compilation);

        if (dependencyMetadataName is null)
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.QueryHandlerImpurity,
            location,
            handlerType.Name,
            dependencyMetadataName));
    }

    /// <summary>
    ///     Gets the impure dependency metadata name implemented by the dependency type, if any.
    /// </summary>
    /// <param name="dependencyType">The dependency type symbol.</param>
    /// <returns>The impure dependency metadata name, if matched.</returns>
    private static string? GetImpureDependencyMetadataName(ITypeSymbol dependencyType, Compilation compilation)
    {
        foreach (var metadataName in LiteBusSymbols.ImpureDependencyMetadataNames)
        {
            var expectedType = compilation.GetTypeByMetadataName(metadataName);

            if (expectedType is null)
            {
                continue;
            }

            if (SymbolEqualityComparer.Default.Equals(dependencyType, expectedType))
            {
                return metadataName;
            }

            foreach (var candidate in dependencyType.AllInterfaces)
            {
                if (SymbolEqualityComparer.Default.Equals(candidate, expectedType))
                {
                    return metadataName;
                }
            }
        }

        return null;
    }
}
