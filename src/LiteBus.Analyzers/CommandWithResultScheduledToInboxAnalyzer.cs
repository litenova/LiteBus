using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using LiteBus.Analyzers.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace LiteBus.Analyzers;

/// <summary>
///     Reports commands with result types stored through the inbox API.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CommandWithResultScheduledToInboxAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        [DiagnosticDescriptors.CommandWithResultScheduledToInbox];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    /// <summary>
    ///     Reports inbox writes that use commands with result types.
    /// </summary>
    /// <param name="context">The syntax node analysis context.</param>
    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not InvocationExpressionSyntax invocation)
        {
            return;
        }

        var symbol = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol as IMethodSymbol;

        if (symbol is null ||
            !IsInboxWriteMethod(symbol) ||
            !IsInboxAcceptMethod(symbol, context.Compilation))
        {
            return;
        }

        if (symbol.Name == "AcceptBatchAsync")
        {
            foreach (var batchMessage in GetBatchMessageTypes(
                         symbol,
                         invocation,
                         context.SemanticModel,
                         context.CancellationToken))
            {
                ReportWhenCommandHasResult(
                    context,
                    batchMessage.MessageType,
                    batchMessage.Location,
                    context.Compilation);
            }

            return;
        }

        var messageType = GetMessageType(symbol, invocation, context.SemanticModel);

        if (messageType is null)
        {
            return;
        }

        ReportWhenCommandHasResult(context, messageType, invocation.GetLocation(), context.Compilation);
    }

    /// <summary>
    ///     Reports LB1004 when the message type implements <c>ICommand&lt;TResult&gt;</c>.
    /// </summary>
    /// <param name="context">The syntax node analysis context.</param>
    /// <param name="messageType">The message type symbol.</param>
    /// <param name="location">The diagnostic location.</param>
    /// <param name="compilation">The compilation being analyzed.</param>
    private static void ReportWhenCommandHasResult(
        SyntaxNodeAnalysisContext context,
        ITypeSymbol messageType,
        Location location,
        Compilation compilation)
    {
        var resultType = GetCommandResultType(messageType, compilation);

        if (resultType is null)
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.CommandWithResultScheduledToInbox,
            location,
            messageType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            resultType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
    }

    /// <summary>
    ///     Determines whether the invoked method writes commands into the inbox.
    /// </summary>
    /// <param name="method">The invoked method symbol.</param>
    /// <returns><see langword="true" /> when the method accepts inbox messages.</returns>
    private static bool IsInboxWriteMethod(IMethodSymbol method)
    {
        return method.Name is "AcceptAsync" or "AcceptBatchAsync";
    }

    /// <summary>
    ///     Determines whether the method symbol is an inbox acceptance API on
    ///     <c>LiteBus.Inbox.Abstractions.IInbox</c> or <c>ITransactionalInbox</c>.
    /// </summary>
    /// <param name="method">The invoked method symbol.</param>
    /// <param name="compilation">The compilation being analyzed.</param>
    /// <returns><see langword="true" /> when the method accepts messages into the inbox; otherwise, <see langword="false" />.</returns>
    private static bool IsInboxAcceptMethod(IMethodSymbol method, Compilation compilation)
    {
        var inboxInterface = compilation.GetTypeByMetadataName("LiteBus.Inbox.Abstractions.IInbox");
        var transactionalInboxInterface = compilation.GetTypeByMetadataName("LiteBus.Inbox.Abstractions.ITransactionalInbox");

        if (method.ContainingType is null)
        {
            return false;
        }

        if (inboxInterface is not null &&
            (SymbolEqualityComparer.Default.Equals(method.ContainingType, inboxInterface) ||
             method.ContainingType.AllInterfaces.Any(candidate =>
                 SymbolEqualityComparer.Default.Equals(candidate, inboxInterface))))
        {
            return true;
        }

        return transactionalInboxInterface is not null &&
               (SymbolEqualityComparer.Default.Equals(method.ContainingType, transactionalInboxInterface) ||
                method.ContainingType.AllInterfaces.Any(candidate =>
                    SymbolEqualityComparer.Default.Equals(candidate, transactionalInboxInterface)));
    }

    /// <summary>
    ///     Gets the message type passed to inbox acceptance APIs.
    /// </summary>
    /// <param name="method">The invoked method symbol.</param>
    /// <param name="invocation">The invocation syntax.</param>
    /// <param name="semanticModel">The semantic model.</param>
    /// <returns>The message type symbol, if resolved.</returns>
    private static ITypeSymbol? GetMessageType(
        IMethodSymbol method,
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel)
    {
        if (method.TypeArguments.Length > 0)
        {
            return method.TypeArguments[0];
        }

        if (invocation.ArgumentList.Arguments.Count == 0)
        {
            return null;
        }

        return GetMessageTypeFromAcceptItem(
            invocation.ArgumentList.Arguments[0].Expression,
            semanticModel);
    }

    /// <summary>
    ///     Gets message types and diagnostic locations from inbox batch acceptance calls.
    /// </summary>
    /// <param name="method">The invoked method symbol.</param>
    /// <param name="invocation">The invocation syntax.</param>
    /// <param name="semanticModel">The semantic model.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The batch message types paired with diagnostic locations.</returns>
    private static ImmutableArray<(ITypeSymbol MessageType, Location Location)> GetBatchMessageTypes(
        IMethodSymbol method,
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (invocation.ArgumentList.Arguments.Count == 0)
        {
            return ImmutableArray<(ITypeSymbol MessageType, Location Location)>.Empty;
        }

        var batchArgument = invocation.ArgumentList.Arguments[0].Expression;
        var builder = ImmutableArray.CreateBuilder<(ITypeSymbol MessageType, Location Location)>();

        foreach (var element in GetCollectionElementExpressions(
                     batchArgument,
                     semanticModel,
                     cancellationToken))
        {
            var messageType = GetMessageTypeFromAcceptItem(element, semanticModel);

            if (messageType is null)
            {
                continue;
            }

            builder.Add((messageType, element.GetLocation()));
        }

        if (builder.Count == 0 && method.TypeArguments.Length > 0)
        {
            builder.Add((method.TypeArguments[0], invocation.GetLocation()));
        }

        return builder.ToImmutable();
    }

    /// <summary>
    ///     Gets element expressions from array, collection, or list initialization syntax.
    /// </summary>
    /// <param name="expression">The batch argument expression.</param>
    /// <param name="semanticModel">The semantic model used to resolve local symbols.</param>
    /// <param name="cancellationToken">The cancellation token for syntax resolution.</param>
    /// <returns>The element expressions contained in the batch argument.</returns>
    private static IEnumerable<ExpressionSyntax> GetCollectionElementExpressions(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        return GetCollectionElementExpressions(
            expression,
            semanticModel,
            new HashSet<SyntaxNode>(),
            cancellationToken);
    }

    /// <summary>
    ///     Gets collection elements recursively, following local initializers and collection spreads.
    /// </summary>
    /// <param name="expression">The collection expression to inspect.</param>
    /// <param name="semanticModel">The semantic model used to resolve local symbols.</param>
    /// <param name="visited">The syntax nodes already followed while resolving local initializers.</param>
    /// <param name="cancellationToken">The cancellation token for syntax resolution.</param>
    /// <returns>The concrete element expressions that can be resolved at the invocation site.</returns>
    private static IEnumerable<ExpressionSyntax> GetCollectionElementExpressions(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        ISet<SyntaxNode> visited,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!visited.Add(expression))
        {
            yield break;
        }

        switch (expression)
        {
            case ImplicitArrayCreationExpressionSyntax { Initializer: not null } implicitArray:
                foreach (var element in implicitArray.Initializer.Expressions)
                {
                    yield return element;
                }

                yield break;
            case ArrayCreationExpressionSyntax { Initializer: not null } arrayCreation:
                foreach (var element in arrayCreation.Initializer.Expressions)
                {
                    yield return element;
                }

                yield break;
            case ObjectCreationExpressionSyntax { Initializer: not null } objectCreation:
                foreach (var element in objectCreation.Initializer.Expressions)
                {
                    yield return element;
                }

                yield break;
            case ImplicitObjectCreationExpressionSyntax { Initializer: not null } implicitObjectCreation:
                foreach (var element in implicitObjectCreation.Initializer.Expressions)
                {
                    yield return element;
                }

                yield break;
            case CollectionExpressionSyntax collectionExpression:
                foreach (var element in collectionExpression.Elements)
                {
                    if (element is ExpressionElementSyntax expressionElement)
                    {
                        yield return expressionElement.Expression;
                        continue;
                    }

                    if (element is SpreadElementSyntax spreadElement)
                    {
                        foreach (var spreadExpression in GetCollectionElementExpressions(
                                     spreadElement.Expression,
                                     semanticModel,
                                     visited,
                                     cancellationToken))
                        {
                            yield return spreadExpression;
                        }
                    }
                }

                yield break;
            case ParenthesizedExpressionSyntax parenthesizedExpression:
                foreach (var element in GetCollectionElementExpressions(
                             parenthesizedExpression.Expression,
                             semanticModel,
                             visited,
                             cancellationToken))
                {
                    yield return element;
                }

                yield break;
            case CastExpressionSyntax castExpression:
                foreach (var element in GetCollectionElementExpressions(
                             castExpression.Expression,
                             semanticModel,
                             visited,
                             cancellationToken))
                {
                    yield return element;
                }

                yield break;
            case IdentifierNameSyntax identifierName
                when semanticModel.GetSymbolInfo(identifierName, cancellationToken).Symbol is ILocalSymbol local:
                foreach (var syntaxReference in local.DeclaringSyntaxReferences)
                {
                    if (syntaxReference.GetSyntax(cancellationToken) is not VariableDeclaratorSyntax
                        {
                            Initializer.Value: { } initializer
                        })
                    {
                        continue;
                    }

                    foreach (var element in GetCollectionElementExpressions(
                                 initializer,
                                 semanticModel,
                                 visited,
                                 cancellationToken))
                    {
                        yield return element;
                    }
                }

                yield break;
        }
    }

    /// <summary>
    ///     Gets the message type from an inbox acceptance item expression.
    /// </summary>
    /// <param name="expression">The acceptance item expression.</param>
    /// <param name="semanticModel">The semantic model.</param>
    /// <returns>The message type symbol, if resolved.</returns>
    private static ITypeSymbol? GetMessageTypeFromAcceptItem(
        ExpressionSyntax expression,
        SemanticModel semanticModel)
    {
        if (expression is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "From" } } fromInvocation &&
            semanticModel.GetSymbolInfo(fromInvocation).Symbol is IMethodSymbol fromMethod)
        {
            if (fromMethod.TypeArguments.Length > 0)
            {
                return fromMethod.TypeArguments[0];
            }

            if (fromInvocation.ArgumentList.Arguments.Count > 0)
            {
                return semanticModel.GetTypeInfo(fromInvocation.ArgumentList.Arguments[0].Expression).Type;
            }
        }

        var expressionType = semanticModel.GetTypeInfo(expression).Type;

        if (expressionType is INamedTypeSymbol { IsGenericType: true, Name: "InboxAcceptItem", TypeArguments.Length: > 0 } namedType)
        {
            return namedType.TypeArguments[0];
        }

        return expressionType;
    }

    /// <summary>
    ///     Gets the result type when the message implements <c>ICommand&lt;TResult&gt;</c>.
    /// </summary>
    /// <param name="messageType">The message type symbol.</param>
    /// <param name="compilation">The compilation being analyzed.</param>
    /// <returns>The command result type, if present.</returns>
    private static ITypeSymbol? GetCommandResultType(ITypeSymbol messageType, Compilation compilation)
    {
        var commandWithResult = LiteBusSymbols.GetType(compilation, "LiteBus.Commands.Abstractions.ICommand`1");

        if (commandWithResult is null)
        {
            return null;
        }

        foreach (var candidate in messageType.AllInterfaces)
        {
            if (!SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, commandWithResult))
            {
                continue;
            }

            return candidate.TypeArguments[0];
        }

        return null;
    }
}
