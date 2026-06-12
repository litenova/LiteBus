using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using LiteBus.Analyzers.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace LiteBus.Analyzers;

/// <summary>
///     Reports query handlers that depend on side-effecting mediators or durable writers.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class QueryHandlerImpurityAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    ///     Open generic handler interfaces analyzed for impure dependencies.
    /// </summary>
    private static readonly ImmutableArray<string> QueryHandlerInterfaceMetadataNames = ImmutableArray.Create(
        "LiteBus.Queries.Abstractions.IQueryHandler`2",
        "LiteBus.Queries.Abstractions.IStreamQueryHandler`2");

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

        if (!ImplementsAnyQueryHandler(handlerType, context.Compilation))
        {
            return;
        }

        var reportedDependencies = new HashSet<string>(StringComparer.Ordinal);

        foreach (var member in handlerType.GetMembers())
        {
            switch (member)
            {
                case IMethodSymbol { MethodKind: MethodKind.Constructor } constructor:
                    AnalyzeParameters(context, handlerType, constructor.Parameters, reportedDependencies);
                    break;
                case IMethodSymbol method when method.MethodKind is MethodKind.Ordinary or MethodKind.LocalFunction:
                    AnalyzeParameters(context, handlerType, method.Parameters, reportedDependencies);
                    break;
                case IFieldSymbol field:
                    ReportIfImpure(context, handlerType, field.Type, field.Locations.FirstOrDefault(), reportedDependencies);
                    break;
                case IPropertySymbol property:
                    ReportIfImpure(context, handlerType, property.Type, property.Locations.FirstOrDefault(), reportedDependencies);
                    break;
            }
        }
    }

    /// <summary>
    ///     Determines whether the handler type implements a supported query handler interface.
    /// </summary>
    /// <param name="handlerType">The handler type symbol.</param>
    /// <param name="compilation">The compilation being analyzed.</param>
    /// <returns><see langword="true" /> when the type implements a supported query handler interface.</returns>
    private static bool ImplementsAnyQueryHandler(INamedTypeSymbol handlerType, Compilation compilation)
    {
        return QueryHandlerInterfaceMetadataNames.Any(metadataName =>
            LiteBusSymbols.ImplementsGenericInterface(handlerType, compilation, metadataName));
    }

    /// <summary>
    ///     Reports impure dependency types used by method parameters.
    /// </summary>
    /// <param name="context">The symbol analysis context.</param>
    /// <param name="handlerType">The query handler type symbol.</param>
    /// <param name="parameters">The parameters to inspect.</param>
    private static void AnalyzeParameters(
        SymbolAnalysisContext context,
        INamedTypeSymbol handlerType,
        ImmutableArray<IParameterSymbol> parameters,
        HashSet<string> reportedDependencies)
    {
        foreach (var parameter in parameters)
        {
            ReportIfImpure(context, handlerType, parameter.Type, parameter.Locations.FirstOrDefault(), reportedDependencies);
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
        Location? location,
        HashSet<string> reportedDependencies)
    {
        var dependencyMetadataName = GetImpureDependencyMetadataName(dependencyType, context.Compilation);

        if (dependencyMetadataName is null)
        {
            return;
        }

        if (!reportedDependencies.Add(dependencyMetadataName))
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
    /// <param name="compilation">The compilation being analyzed.</param>
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
