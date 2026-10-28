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
/// <remarks>
///     Constructor parameters, fields, and properties are inspected, including members inherited from base handler
///     classes. Ordinary method parameters are not dependencies: a helper that takes a mediator receives it from its
///     caller rather than from injection, so such parameters stay silent.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class QueryHandlerImpurityAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    ///     Open generic handler interfaces analyzed for impure dependencies.
    /// </summary>
    private static readonly ImmutableArray<string> QueryHandlerInterfaceMetadataNames =
    [
        "LiteBus.Queries.Abstractions.IQueryHandler`2",
        "LiteBus.Queries.Abstractions.IStreamQueryHandler`2"
    ];

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        [DiagnosticDescriptors.QueryHandlerImpurity];

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

        for (INamedTypeSymbol? current = handlerType;
            current is not null && current.SpecialType != SpecialType.System_Object;
            current = current.BaseType)
        {
            AnalyzeMembers(context, handlerType, current, reportedDependencies);
        }
    }

    /// <summary>
    ///     Reports impure dependencies declared on one level of the handler type hierarchy.
    /// </summary>
    /// <param name="context">The symbol analysis context.</param>
    /// <param name="handlerType">The query handler type symbol.</param>
    /// <param name="declaringType">The hierarchy level whose members are inspected.</param>
    /// <param name="reportedDependencies">The impure dependency metadata names already reported.</param>
    private static void AnalyzeMembers(
        SymbolAnalysisContext context,
        INamedTypeSymbol handlerType,
        INamedTypeSymbol declaringType,
        HashSet<string> reportedDependencies)
    {
        foreach (var member in declaringType.GetMembers())
        {
            switch (member)
            {
                case IMethodSymbol { MethodKind: MethodKind.Constructor } constructor:
                    AnalyzeParameters(context, handlerType, constructor.Parameters, reportedDependencies);
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
    ///     Reports impure dependency types used by constructor parameters.
    /// </summary>
    /// <param name="context">The symbol analysis context.</param>
    /// <param name="handlerType">The query handler type symbol.</param>
    /// <param name="parameters">The parameters to inspect.</param>
    /// <param name="reportedDependencies">The impure dependency metadata names already reported.</param>
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
    /// <param name="reportedDependencies">The impure dependency metadata names already reported.</param>
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

        var diagnosticLocation = LiteBusSymbols.GetDiagnosticLocation(
            context.Compilation,
            location ?? Location.None);

        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.QueryHandlerImpurity,
            diagnosticLocation,
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

            if (dependencyType is INamedTypeSymbol namedDependency &&
                SymbolEqualityComparer.Default.Equals(namedDependency.OriginalDefinition, expectedType))
            {
                return metadataName;
            }

            foreach (var candidate in dependencyType.AllInterfaces)
            {
                if (SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, expectedType))
                {
                    return metadataName;
                }
            }
        }

        return null;
    }
}
