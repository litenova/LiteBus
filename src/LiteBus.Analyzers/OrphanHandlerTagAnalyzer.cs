using System.Collections.Immutable;
using System.Linq;
using LiteBus.Analyzers.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace LiteBus.Analyzers;

/// <summary>
///     Reports handler tags that are not referenced by command or event mediation filters in the compilation.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class OrphanHandlerTagAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(DiagnosticDescriptors.OrphanHandlerTag);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(startContext =>
        {
            var referencedTags = TagReferenceAnalysis.CollectReferencedTags(startContext.Compilation);

            startContext.RegisterSyntaxNodeAction(
                nodeContext =>
                {
                    if (nodeContext.SemanticModel.GetDeclaredSymbol(
                            (TypeDeclarationSyntax)nodeContext.Node,
                            nodeContext.CancellationToken) is not INamedTypeSymbol symbol)
                    {
                        return;
                    }

                    if (!HandlerAnalysis.IsHandlerType(symbol, startContext.Compilation))
                    {
                        return;
                    }

                    foreach (var attribute in symbol.GetAttributes())
                    {
                        if (!IsHandlerTagAttribute(attribute.AttributeClass))
                        {
                            continue;
                        }

                        foreach (var argument in attribute.ConstructorArguments)
                        {
                            if (argument.Kind == TypedConstantKind.Primitive && argument.Value is string tag && !referencedTags.Contains(tag))
                            {
                                nodeContext.ReportDiagnostic(Diagnostic.Create(
                                    DiagnosticDescriptors.OrphanHandlerTag,
                                    symbol.Locations.FirstOrDefault() ?? Location.None,
                                    symbol.Name,
                                    tag));
                            }
                        }

                        if (attribute.ConstructorArguments.Length == 1 &&
                            attribute.ConstructorArguments[0].Kind == TypedConstantKind.Array &&
                            attribute.ConstructorArguments[0].Values is { Length: > 0 } values)
                        {
                            foreach (var value in values)
                            {
                                if (value.Value is string tag && !referencedTags.Contains(tag))
                                {
                                    nodeContext.ReportDiagnostic(Diagnostic.Create(
                                        DiagnosticDescriptors.OrphanHandlerTag,
                                        symbol.Locations.FirstOrDefault() ?? Location.None,
                                        symbol.Name,
                                        tag));
                                }
                            }
                        }
                    }
                },
                SyntaxKind.ClassDeclaration,
                SyntaxKind.StructDeclaration,
                SyntaxKind.RecordDeclaration);
        });
    }

    /// <summary>
    ///     Determines whether the attribute type is a handler tag attribute.
    /// </summary>
    /// <param name="attributeClass">The attribute class symbol.</param>
    /// <returns><see langword="true" /> when the attribute declares handler tags.</returns>
    private static bool IsHandlerTagAttribute(INamedTypeSymbol? attributeClass)
    {
        while (attributeClass is not null)
        {
            if (attributeClass.Name is "HandlerTagAttribute" or "HandlerTagsAttribute")
            {
                return true;
            }

            attributeClass = attributeClass.BaseType;
        }

        return false;
    }
}
