using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using LiteBus.Analyzers.Analysis;

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
        context.RegisterCompilationAction(AnalyzeCompilation);
    }

    /// <summary>
    ///     Reports handler tags that are not referenced by mediation filters.
    /// </summary>
    /// <param name="context">The compilation analysis context.</param>
    private static void AnalyzeCompilation(CompilationAnalysisContext context)
    {
        var referencedTags = TagReferenceAnalysis.CollectReferencedTags(context.Compilation);
        var handlerTags = CollectHandlerTags(context.Compilation, context.CancellationToken);

        foreach (var (handlerType, tag, location) in handlerTags)
        {
            if (referencedTags.Contains(tag))
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.OrphanHandlerTag,
                location,
                handlerType.Name,
                tag));
        }
    }

    /// <summary>
    ///     Collects handler tags declared on handler types in the compilation.
    /// </summary>
    /// <param name="compilation">The compilation being analyzed.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Handler type, tag, and location tuples.</returns>
    private static ImmutableArray<(INamedTypeSymbol HandlerType, string Tag, Location Location)> CollectHandlerTags(
        Compilation compilation,
        System.Threading.CancellationToken cancellationToken)
    {
        var builder = ImmutableArray.CreateBuilder<(INamedTypeSymbol, string, Location)>();

        foreach (var tree in compilation.SyntaxTrees)
        {
            var semanticModel = compilation.GetSemanticModel(tree);

            foreach (var typeDeclaration in tree.GetRoot(cancellationToken).DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                var symbol = semanticModel.GetDeclaredSymbol(typeDeclaration, cancellationToken) as INamedTypeSymbol;

                if (symbol is null || !HandlerAnalysis.IsHandlerType(symbol, compilation))
                {
                    continue;
                }

                foreach (var attribute in symbol.GetAttributes())
                {
                    if (!IsHandlerTagAttribute(attribute.AttributeClass))
                    {
                        continue;
                    }

                    foreach (var argument in attribute.ConstructorArguments)
                    {
                        if (argument.Kind == TypedConstantKind.Primitive && argument.Value is string tag)
                        {
                            builder.Add((symbol, tag, symbol.Locations.FirstOrDefault() ?? Location.None));
                        }
                    }

                    if (attribute.ConstructorArguments.Length == 1 &&
                        attribute.ConstructorArguments[0].Kind == TypedConstantKind.Array &&
                        attribute.ConstructorArguments[0].Values is { Length: > 0 } values)
                    {
                        foreach (var value in values)
                        {
                            if (value.Value is string tag)
                            {
                                builder.Add((symbol, tag, symbol.Locations.FirstOrDefault() ?? Location.None));
                            }
                        }
                    }
                }
            }
        }

        return builder.ToImmutable();
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
