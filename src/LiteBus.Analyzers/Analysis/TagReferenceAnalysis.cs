using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LiteBus.Analyzers.Analysis;

/// <summary>
///     Semantic helpers for command and event mediation tag references.
/// </summary>
internal static class TagReferenceAnalysis
{
    /// <summary>
    ///     Collects tag strings referenced by command and event mediation settings in the compilation.
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
        }

        return tags;
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

        if (initializer.Parent is AssignmentExpressionSyntax assignment &&
            assignment.Left is MemberAccessExpressionSyntax memberAccess &&
            memberAccess.Name.Identifier.Text == "Tags")
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

        var containingType = property.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        return containingType is
            "global::LiteBus.Commands.Abstractions.CommandMediationSettings.CommandMediationFilters" or
            "LiteBus.Commands.Abstractions.CommandMediationSettings.CommandMediationFilters" or
            "global::LiteBus.Events.Abstractions.EventMediationRoutingSettings" or
            "LiteBus.Events.Abstractions.EventMediationRoutingSettings";
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
