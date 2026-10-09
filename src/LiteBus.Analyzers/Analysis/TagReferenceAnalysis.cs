using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
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
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The referenced tag strings.</returns>
    internal static HashSet<string> CollectReferencedTags(
        Compilation compilation,
        CancellationToken cancellationToken = default)
    {
        var tags = new HashSet<string>(StringComparer.Ordinal);

        foreach (var tree in compilation.SyntaxTrees)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var model = compilation.GetSemanticModel(tree);
            var root = tree.GetRoot(cancellationToken);

            foreach (var assignment in root.DescendantNodes().OfType<AssignmentExpressionSyntax>())
            {
                if (!IsMediationTagsAssignment(assignment, model, cancellationToken))
                {
                    continue;
                }

                CollectStringLiterals(assignment.Right, model, tags, cancellationToken);
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

                    var typeInfo = model.GetTypeInfo(objectCreation, cancellationToken);
                    if (typeInfo.Type is not INamedTypeSymbol namedType ||
                        !IsMediationTagsContainerType(namedType))
                    {
                        continue;
                    }

                    CollectStringLiterals(memberAssignment.Right, model, tags, cancellationToken);
                }
            }

            foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                CollectTagFromMediatorExtensionInvocation(invocation, model, tags, cancellationToken);
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
    /// <param name="cancellationToken">The cancellation token.</param>
    private static void CollectTagFromMediatorExtensionInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        HashSet<string> tags,
        CancellationToken cancellationToken)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess ||
            !TagExtensionMethodNames.Contains(memberAccess.Name.Identifier.Text))
        {
            return;
        }

        if (TryCollectTagFromOperation(invocation, semanticModel, tags, cancellationToken))
        {
            return;
        }

        CollectTagFromSymbolFallback(invocation, semanticModel, tags, cancellationToken);
    }

    /// <summary>
    ///     Collects the tag argument through operation-based argument-to-parameter mapping.
    /// </summary>
    /// <param name="invocation">The invocation expression syntax.</param>
    /// <param name="semanticModel">The semantic model.</param>
    /// <param name="tags">The tag collection to populate.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true" /> when an operation resolved the invocation; otherwise, <see langword="false" />.</returns>
    private static bool TryCollectTagFromOperation(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        HashSet<string> tags,
        CancellationToken cancellationToken)
    {
        if (semanticModel.GetOperation(invocation, cancellationToken) is not IInvocationOperation operation)
        {
            return false;
        }

        var unreduced = operation.TargetMethod.ReducedFrom ?? operation.TargetMethod;

        if (!unreduced.IsExtensionMethod || !IsTagExtensionMethod(unreduced))
        {
            return true;
        }

        foreach (var argument in operation.Arguments)
        {
            if (argument.Parameter is not null &&
                argument.Parameter.Type.SpecialType == SpecialType.System_String &&
                argument.Parameter.Name.Equals("tag", StringComparison.Ordinal) &&
                argument.Value.Syntax is ExpressionSyntax tagExpression)
            {
                CollectStringLiterals(tagExpression, semanticModel, tags, cancellationToken);
                break;
            }
        }

        return true;
    }

    /// <summary>
    ///     Collects the tag argument by matching the invocation argument list against the resolved parameter list.
    /// </summary>
    /// <param name="invocation">The invocation expression syntax.</param>
    /// <param name="semanticModel">The semantic model.</param>
    /// <param name="tags">The tag collection to populate.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    private static void CollectTagFromSymbolFallback(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        HashSet<string> tags,
        CancellationToken cancellationToken)
    {
        if (semanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol methodSymbol)
        {
            return;
        }

        var unreduced = methodSymbol.ReducedFrom ?? methodSymbol;

        if (!unreduced.IsExtensionMethod || !IsTagExtensionMethod(unreduced))
        {
            return;
        }

        var parameters = methodSymbol.Parameters;

        for (var index = 0; index < parameters.Length && index < invocation.ArgumentList.Arguments.Count; index++)
        {
            var parameter = parameters[index];

            if (parameter.Type.SpecialType == SpecialType.System_String &&
                parameter.Name.Equals("tag", StringComparison.Ordinal))
            {
                CollectStringLiterals(
                    invocation.ArgumentList.Arguments[index].Expression,
                    semanticModel,
                    tags,
                    cancellationToken);
                return;
            }
        }
    }

    /// <summary>
    ///     Determines whether the extension method carries a mediator tag argument.
    /// </summary>
    /// <param name="methodSymbol">The unreduced extension method symbol.</param>
    /// <returns><see langword="true" /> when the method is a recognized mediator tag extension.</returns>
    private static bool IsTagExtensionMethod(IMethodSymbol methodSymbol)
    {
        if (methodSymbol.ContainingType.Name is
            "CommandMediatorExtensions" or
            "QueryMediatorExtensions" or
            "EventMediatorExtensions")
        {
            return true;
        }

        return methodSymbol.Parameters.Length > 0 &&
            methodSymbol.Parameters[0].Type.OriginalDefinition.ToDisplayString() is
                "LiteBus.Commands.Abstractions.ICommandMediator" or
                "LiteBus.Queries.Abstractions.IQueryMediator" or
                "LiteBus.Events.Abstractions.IEventMediator";
    }

    /// <summary>
    ///     Determines whether an assignment targets a mediation tag collection.
    /// </summary>
    /// <param name="assignment">The assignment expression syntax.</param>
    /// <param name="semanticModel">The semantic model.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true" /> when the assignment configures mediation tags.</returns>
    private static bool IsMediationTagsAssignment(
        AssignmentExpressionSyntax assignment,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (assignment.Left is not MemberAccessExpressionSyntax memberAccess ||
            memberAccess.Name.Identifier.Text != "Tags")
        {
            return false;
        }

        var symbol = semanticModel.GetSymbolInfo(memberAccess.Name, cancellationToken).Symbol as IPropertySymbol;

        return symbol is not null && IsMediationTagsProperty(symbol);
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
    ///     Collects string literals and constant string references from an expression subtree.
    /// </summary>
    /// <param name="expression">The expression syntax.</param>
    /// <param name="semanticModel">The semantic model.</param>
    /// <param name="tags">The tag collection to populate.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    private static void CollectStringLiterals(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        HashSet<string> tags,
        CancellationToken cancellationToken)
    {
        foreach (var node in expression.DescendantNodesAndSelf())
        {
            cancellationToken.ThrowIfCancellationRequested();

            switch (node)
            {
                case LiteralExpressionSyntax literal when literal.Token.Value is string value:
                    tags.Add(value);
                    break;
                case IdentifierNameSyntax identifier:
                    AddConstantString(identifier, semanticModel, tags, cancellationToken);
                    break;
                case MemberAccessExpressionSyntax memberAccess:
                    AddConstantString(memberAccess, semanticModel, tags, cancellationToken);
                    break;
            }
        }
    }

    /// <summary>
    ///     Adds the constant string value of an expression to the tag collection when it has one.
    /// </summary>
    /// <param name="expression">The expression syntax.</param>
    /// <param name="semanticModel">The semantic model.</param>
    /// <param name="tags">The tag collection to populate.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    private static void AddConstantString(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        HashSet<string> tags,
        CancellationToken cancellationToken)
    {
        var constant = semanticModel.GetConstantValue(expression, cancellationToken);

        if (constant.HasValue && constant.Value is string value)
        {
            tags.Add(value);
        }
    }
}
