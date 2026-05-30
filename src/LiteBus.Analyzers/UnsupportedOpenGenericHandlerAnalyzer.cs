using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using LiteBus.Analyzers.Analysis;

namespace LiteBus.Analyzers;

/// <summary>
///     Reports unsupported open generic handler shapes.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UnsupportedOpenGenericHandlerAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(DiagnosticDescriptors.UnsupportedOpenGenericHandler);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
        context.RegisterSyntaxNodeAction(AnalyzeTypeOfExpression, SyntaxKind.TypeOfExpression);
    }

    /// <summary>
    ///     Reports unsupported open generic handler type declarations.
    /// </summary>
    /// <param name="context">The symbol analysis context.</param>
    private static void AnalyzeNamedType(SymbolAnalysisContext context)
    {
        if (context.Symbol is not INamedTypeSymbol handlerType)
        {
            return;
        }

        if (!HandlerAnalysis.IsUnsupportedOpenGenericHandler(handlerType))
        {
            return;
        }

        var openDefinition = HandlerAnalysis.IsGenericTypeDefinition(handlerType)
            ? handlerType
            : handlerType.OriginalDefinition;

        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.UnsupportedOpenGenericHandler,
            handlerType.Locations.FirstOrDefault() ?? Location.None,
            openDefinition.Name,
            openDefinition.TypeParameters.Length));
    }

    /// <summary>
    ///     Reports unsupported open generic handler registrations through <c>typeof</c> expressions.
    /// </summary>
    /// <param name="context">The syntax node analysis context.</param>
    private static void AnalyzeTypeOfExpression(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not TypeOfExpressionSyntax typeOfExpression)
        {
            return;
        }

        var typeInfo = context.SemanticModel.GetTypeInfo(typeOfExpression.Type, context.CancellationToken);

        if (typeInfo.Type is not INamedTypeSymbol handlerType ||
            !HandlerAnalysis.IsUnsupportedOpenGenericHandler(handlerType))
        {
            return;
        }

        var openDefinition = HandlerAnalysis.IsGenericTypeDefinition(handlerType)
            ? handlerType
            : handlerType.OriginalDefinition;

        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.UnsupportedOpenGenericHandler,
            typeOfExpression.GetLocation(),
            openDefinition.Name,
            openDefinition.TypeParameters.Length));
    }
}
