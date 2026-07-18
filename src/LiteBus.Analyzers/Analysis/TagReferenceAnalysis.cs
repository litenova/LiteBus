using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LiteBus.Analyzers.Analysis;

/// <summary>
///     Semantic helpers for command, query, and event mediation tag references.
/// </summary>
internal static class TagReferenceAnalysis
{
    /// <summary>
    ///     Mediator extension method names that accept a tag argument.
    /// </summary>
    private static readonly HashSet<string> TagExtensionMethodNames = new(StringComparer.Ordinal)
    {
        "SendAsync",
        "QueryAsync",
        "PublishAsync",
        "StreamAsync"
    };

    /// <summary>
    ///     Collects tag strings referenced by command, query, and event mediation settings in the compilation.
    /// </summary>
    /// <param name="compilation">The compilation being analyzed.</param>
    /// <returns>The referenced tag strings.</returns>
    internal static HashSet<string> CollectReferencedTags(Compilation compilation)
    {
        var tags = new HashSet<string>(StringComparer.Ordinal);

        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            var root = tree.GetRoot();

            foreach (var assignment in root.DescendantNodes().OfType<AssignmentExpressionSyntax>())
            {
                if (!IsMediationTagsAssignment(assignment, model))
                {
                    continue;
                }

                CollectStringLiterals(assignment.Right, tags);
            }

            foreach (var initializer in root.DescendantNodes().OfType<InitializerExpressionSyntax>())
            {
                if (!IsMediationTagsInitializer(initializer, model))
                {
                    continue;
                }

                CollectStringLiterals(initializer, tags);
            }

            foreach (var objectCreation in root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
            {
                if (objectCreation.Initializer is null)
                {
                    continue;
                }

                foreach (var expression in objectCreation.Initializer.Expressions)
                {
                    if (expression is not AssignmentExpressionSyntax memberAssignment ||
                        memberAssignment.Left is not IdentifierNameSyntax identifier ||
                        identifier.Identifier.Text != "Tags")
                    {
                        continue;
                    }

                    var typeInfo = model.GetTypeInfo(objectCreation);
                    if (typeInfo.Type is not INamedTypeSymbol namedType ||
                        !IsMediationTagsContainerType(namedType))
                    {
                        continue;
                    }

                    CollectStringLiterals(memberAssignment.Right, tags);
                }
            }

            foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                CollectTagFromMediatorExtensionInvocation(invocation, model, tags);
            }
        }

        return tags;
    }

    /// <summary>
    ///     Collects a tag literal from mediator extension method invocations such as <c>SendAsync(command, "tag")</c>.
    /// </summary>
    /// <param name="invocation">The invocation expression syntax.</param>
    /// <param name="semanticModel">The semantic model.</param>
    /// <param name="tags">The tag collection to populate.</param>
    private static void CollectTagFromMediatorExtensionInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        HashSet<string> tags)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess ||
            !TagExtensionMethodNames.Contains(memberAccess.Name.Identifier.Text))
        {
            return;
        }

        if (semanticModel.GetSymbolInfo(memberAccess.Name).Symbol is IMethodSymbol { IsExtensionMethod: true } methodSymbol &&
            IsTagExtensionContainingType(methodSymbol.ContainingType))
        {
            var tagParameterIndex = GetTagParameterIndex(methodSymbol);

            if (tagParameterIndex >= 0 && tagParameterIndex < invocation.ArgumentList.Arguments.Count)
            {
                var countBefore = tags.Count;
                CollectStringLiterals(invocation.ArgumentList.Arguments[tagParameterIndex].Expression, tags);

                if (tags.Count > countBefore)
                {
                    return;
                }
            }
        }

        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            CollectStringLiterals(argument.Expression, tags);
        }
    }

    /// <summary>
    ///     Gets the parameter index that carries the tag value for a mediator extension overload.
    /// </summary>
    /// <param name="methodSymbol">The extension method symbol.</param>
    /// <returns>The zero-based tag parameter index, or <c>-1</c> when the overload has no tag parameter.</returns>
    private static int GetTagParameterIndex(IMethodSymbol methodSymbol)
    {
        for (var index = 0; index < methodSymbol.Parameters.Length; index++)
        {
            var parameter = methodSymbol.Parameters[index];

            if (parameter.Type.SpecialType == SpecialType.System_String &&
                parameter.Name.Equals("tag", StringComparison.Ordinal))
            {
                return index - 1;
            }
        }

        return -1;
    }

    /// <summary>
    ///     Determines whether the extension method belongs to a mediator extension type.
    /// </summary>
    /// <param name="containingType">The extension class symbol.</param>
    /// <returns><see langword="true" /> when the type declares mediator tag extension methods.</returns>
    private static bool IsTagExtensionContainingType(INamedTypeSymbol containingType)
    {
        var displayName = containingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        return displayName is
            "global::LiteBus.Commands.Abstractions.CommandMediatorExtensions" or
            "LiteBus.Commands.Abstractions.CommandMediatorExtensions" or
            "global::LiteBus.Queries.Abstractions.QueryMediatorExtensions" or
            "LiteBus.Queries.Abstractions.QueryMediatorExtensions" or
            "global::LiteBus.Events.Abstractions.EventMediatorExtensions" or
            "LiteBus.Events.Abstractions.EventMediatorExtensions";
    }

    /// <summary>
    ///     Determines whether an assignment targets a mediation tag collection.
    /// </summary>
    /// <param name="assignment">The assignment expression syntax.</param>
    /// <param name="semanticModel">The semantic model.</param>
    /// <returns><see langword="true" /> when the assignment configures mediation tags.</returns>
    private static bool IsMediationTagsAssignment(AssignmentExpressionSyntax assignment, SemanticModel semanticModel)
    {
        if (assignment.Left is not MemberAccessExpressionSyntax memberAccess ||
            memberAccess.Name.Identifier.Text != "Tags")
        {
            return false;
        }

        var symbol = semanticModel.GetSymbolInfo(memberAccess.Name).Symbol as IPropertySymbol;

        return symbol is not null && IsMediationTagsProperty(symbol);
    }

    /// <summary>
    ///     Determines whether an initializer configures a mediation tag collection.
    /// </summary>
    /// <param name="initializer">The initializer expression syntax.</param>
    /// <param name="semanticModel">The semantic model.</param>
    /// <returns><see langword="true" /> when the initializer configures mediation tags.</returns>
    private static bool IsMediationTagsInitializer(InitializerExpressionSyntax initializer, SemanticModel semanticModel)
    {
        if (initializer.Parent is not AnonymousObjectMemberDeclaratorSyntax &&
            initializer.Parent is not InitializerExpressionSyntax &&
            initializer.Parent is not AssignmentExpressionSyntax)
        {
            return false;
        }

        if (initializer.Parent is AssignmentExpressionSyntax { Left: MemberAccessExpressionSyntax { Name.Identifier.Text: "Tags" } memberAccess })
        {
            var symbol = semanticModel.GetSymbolInfo(memberAccess.Name).Symbol as IPropertySymbol;

            return symbol is not null && IsMediationTagsProperty(symbol);
        }

        return false;
    }

    /// <summary>
    ///     Determines whether the property symbol is a mediation tag filter property.
    /// </summary>
    /// <param name="property">The property symbol.</param>
    /// <returns><see langword="true" /> when the property stores mediation tags.</returns>
    private static bool IsMediationTagsProperty(IPropertySymbol property)
    {
        if (property.Name != "Tags")
        {
            return false;
        }

        return IsMediationTagsContainerType(property.ContainingType);
    }

    /// <summary>
    ///     Determines whether the type owns a mediation tag collection property.
    /// </summary>
    /// <param name="type">The type symbol to inspect.</param>
    /// <returns><see langword="true" /> when the type stores mediation tags.</returns>
    private static bool IsMediationTagsContainerType(INamedTypeSymbol type)
    {
        var containingType = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        return containingType is
            "global::LiteBus.Commands.Abstractions.CommandRoutingSettings" or
            "LiteBus.Commands.Abstractions.CommandRoutingSettings" or
            "global::LiteBus.Queries.Abstractions.QueryRoutingSettings" or
            "LiteBus.Queries.Abstractions.QueryRoutingSettings" or
            "global::LiteBus.Events.Abstractions.EventRoutingSettings" or
            "LiteBus.Events.Abstractions.EventRoutingSettings";
    }

    /// <summary>
    ///     Collects string literals from an expression subtree.
    /// </summary>
    /// <param name="expression">The expression syntax.</param>
    /// <param name="tags">The tag collection to populate.</param>
    private static void CollectStringLiterals(ExpressionSyntax expression, HashSet<string> tags)
    {
        foreach (var literal in expression.DescendantNodesAndSelf().OfType<LiteralExpressionSyntax>())
        {
            if (literal.Token.Value is string value)
            {
                tags.Add(value);
            }
        }
    }
}
