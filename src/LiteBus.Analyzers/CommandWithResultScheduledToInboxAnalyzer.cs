using System.Collections.Immutable;
using System.Linq;
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
        ImmutableArray.Create(DiagnosticDescriptors.CommandWithResultScheduledToInbox);

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

        var messageType = GetMessageType(symbol, invocation, context.SemanticModel);

        if (messageType is null)
        {
            return;
        }

        var resultType = GetCommandResultType(messageType, context.Compilation);

        if (resultType is null)
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.CommandWithResultScheduledToInbox,
            invocation.GetLocation(),
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

        var firstArgumentType = semanticModel.GetTypeInfo(invocation.ArgumentList.Arguments[0].Expression).Type;

        if (firstArgumentType is INamedTypeSymbol namedType &&
            namedType.IsGenericType &&
            namedType.Name == "InboxAcceptItem" &&
            namedType.TypeArguments.Length > 0)
        {
            return namedType.TypeArguments[0];
        }

        return firstArgumentType;
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