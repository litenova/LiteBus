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
///     Reports handler tags that are not referenced by command or event mediation filters in the compilation.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class OrphanHandlerTagAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        [DiagnosticDescriptors.OrphanHandlerTag];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(AnalyzeCompilationStart);
    }

    /// <summary>
    ///     Gathers referenced tags and tagged handler declarations for one compilation.
    /// </summary>
    /// <param name="context">The compilation start analysis context.</param>
    private static void AnalyzeCompilationStart(CompilationStartAnalysisContext context)
    {
        var typeCache = new LiteBusTypeCache(context.Compilation);
        var tagCollector = new TagReferenceCollector();
        var occurrences = new ConcurrentBag<TagHandlerOccurrence>();

        context.RegisterSyntaxNodeAction(
            nodeContext => AnalyzeAssignment(nodeContext, tagCollector),
            SyntaxKind.SimpleAssignmentExpression,
            SyntaxKind.AddAssignmentExpression,
            SyntaxKind.SubtractAssignmentExpression,
            SyntaxKind.MultiplyAssignmentExpression,
            SyntaxKind.DivideAssignmentExpression,
            SyntaxKind.ModuloAssignmentExpression,
            SyntaxKind.AndAssignmentExpression,
            SyntaxKind.ExclusiveOrAssignmentExpression,
            SyntaxKind.OrAssignmentExpression,
            SyntaxKind.LeftShiftAssignmentExpression,
            SyntaxKind.RightShiftAssignmentExpression,
            SyntaxKind.UnsignedRightShiftAssignmentExpression,
            SyntaxKind.CoalesceAssignmentExpression);

        context.RegisterSyntaxNodeAction(
            nodeContext => AnalyzeObjectCreation(nodeContext, tagCollector),
            SyntaxKind.ObjectCreationExpression,
            SyntaxKind.ImplicitObjectCreationExpression);

        context.RegisterSyntaxNodeAction(
            nodeContext => AnalyzeInvocation(nodeContext, tagCollector),
            SyntaxKind.InvocationExpression);

        context.RegisterSyntaxNodeAction(
            nodeContext => AnalyzeHandlerDeclaration(nodeContext, typeCache, occurrences),
            SyntaxKind.ClassDeclaration,
            SyntaxKind.StructDeclaration,
            SyntaxKind.RecordDeclaration);

        context.RegisterCompilationEndAction(
            endContext => AnalyzeCompilationEnd(endContext, tagCollector, occurrences));
    }

    /// <summary>
    ///     Collects referenced tags from one assignment node.
    /// </summary>
    /// <param name="context">The syntax node analysis context.</param>
    /// <param name="tags">The tag collection to populate.</param>
    private static void AnalyzeAssignment(SyntaxNodeAnalysisContext context, TagReferenceCollector tags)
    {
        TagReferenceAnalysis.CollectTagsFromAssignment(
            (AssignmentExpressionSyntax) context.Node,
            context.SemanticModel,
            tags,
            context.CancellationToken);
    }

    /// <summary>
    ///     Collects referenced tags from one object creation node.
    /// </summary>
    /// <param name="context">The syntax node analysis context.</param>
    /// <param name="tags">The tag collection to populate.</param>
    private static void AnalyzeObjectCreation(SyntaxNodeAnalysisContext context, TagReferenceCollector tags)
    {
        TagReferenceAnalysis.CollectTagsFromObjectCreation(
            (BaseObjectCreationExpressionSyntax) context.Node,
            context.SemanticModel,
            tags,
            context.CancellationToken);
    }

    /// <summary>
    ///     Collects referenced tags from one invocation node.
    /// </summary>
    /// <param name="context">The syntax node analysis context.</param>
    /// <param name="tags">The tag collection to populate.</param>
    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context, TagReferenceCollector tags)
    {
        TagReferenceAnalysis.CollectTagsFromInvocation(
            (InvocationExpressionSyntax) context.Node,
            context.SemanticModel,
            tags,
            context.CancellationToken);
    }

    /// <summary>
    ///     Records the handler tags declared on one type declaration node.
    /// </summary>
    /// <param name="context">The syntax node analysis context.</param>
    /// <param name="typeCache">The per-compilation type cache.</param>
    /// <param name="occurrences">The handler occurrences to populate.</param>
    private static void AnalyzeHandlerDeclaration(
        SyntaxNodeAnalysisContext context,
        LiteBusTypeCache typeCache,
        ConcurrentBag<TagHandlerOccurrence> occurrences)
    {
        if (context.Node is not TypeDeclarationSyntax declaration)
        {
            return;
        }

        if (context.SemanticModel.GetDeclaredSymbol(declaration, context.CancellationToken) is not INamedTypeSymbol symbol)
        {
            return;
        }

        if (!HandlerAnalysis.IsHandlerType(symbol, typeCache))
        {
            return;
        }

        var location = LiteBusSymbols.GetDiagnosticLocation(
            context.Compilation,
            symbol.Locations.FirstOrDefault(candidate => candidate.IsInSource) ??
            symbol.Locations.FirstOrDefault() ??
            Location.None);

        var tags = GetHandlerTags(symbol);

        if (tags.Length == 0)
        {
            return;
        }

        occurrences.Add(new TagHandlerOccurrence(symbol.Name, location, tags));
    }

    /// <summary>
    ///     Reports the handler tags that no mediation filter referenced.
    /// </summary>
    /// <param name="context">The compilation analysis context.</param>
    /// <param name="tags">The collected referenced tags.</param>
    /// <param name="occurrences">The collected handler occurrences.</param>
    private static void AnalyzeCompilationEnd(
        CompilationAnalysisContext context,
        TagReferenceCollector tags,
        ConcurrentBag<TagHandlerOccurrence> occurrences)
    {
        if (occurrences.IsEmpty)
        {
            return;
        }

        // Node actions run concurrently, so order occurrences the way source order presents them:
        // by file path, then by span start, then by handler name. Each occurrence keeps its
        // declaration so a partial handler with two declarations still reports twice.
        var ordered = occurrences
            .OrderBy(occurrence => occurrence.Location.SourceTree?.FilePath ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(occurrence => occurrence.Location.SourceSpan.Start)
            .ThenBy(occurrence => occurrence.HandlerName, StringComparer.Ordinal)
            .ToList();

        foreach (var occurrence in ordered)
        {
            foreach (var tag in occurrence.Tags)
            {
                if (!tags.Contains(tag))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        DiagnosticDescriptors.OrphanHandlerTag,
                        occurrence.Location,
                        occurrence.HandlerName,
                        tag));
                }
            }
        }
    }

    /// <summary>
    ///     Gets the handler tags declared on the symbol in attribute and argument order.
    /// </summary>
    /// <param name="symbol">The handler type symbol.</param>
    /// <returns>The handler tags in report order.</returns>
    private static ImmutableArray<string> GetHandlerTags(INamedTypeSymbol symbol)
    {
        var builder = ImmutableArray.CreateBuilder<string>();

        foreach (var attribute in symbol.GetAttributes())
        {
            if (!IsHandlerTagAttribute(attribute.AttributeClass))
            {
                continue;
            }

            foreach (var argument in attribute.ConstructorArguments)
            {
                if (argument is { Kind: TypedConstantKind.Primitive, Value: string tag })
                {
                    builder.Add(tag);
                }
            }

            if (attribute.ConstructorArguments.Length == 1
                && attribute.ConstructorArguments[0] is { Kind: TypedConstantKind.Array, Values: { Length: > 0 } values })
            {
                foreach (var value in values)
                {
                    if (value.Value is string tag)
                    {
                        builder.Add(tag);
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
