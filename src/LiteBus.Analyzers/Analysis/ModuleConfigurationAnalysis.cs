using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LiteBus.Analyzers.Analysis;

/// <summary>
///     Semantic helpers for inbox and outbox module builder configuration.
/// </summary>
internal static class ModuleConfigurationAnalysis
{
    /// <summary>
    ///     Determines whether the enclosing scope configures a dispatcher for the supplied processor enablement call.
    /// </summary>
    /// <param name="enableProcessorInvocation">The processor enablement invocation.</param>
    /// <param name="semanticModel">The semantic model.</param>
    /// <param name="compilation">The compilation being analyzed.</param>
    /// <returns><see langword="true" /> when a dispatcher is configured in the same scope.</returns>
    internal static bool HasDispatcherConfigurationInScope(
        InvocationExpressionSyntax enableProcessorInvocation,
        SemanticModel semanticModel,
        Compilation compilation)
    {
        var scope = GetConfigurationScope(enableProcessorInvocation);

        if (scope is null)
        {
            return false;
        }

        var isInbox = IsProcessorEnablement(enableProcessorInvocation, semanticModel, true);

        foreach (var invocation in scope.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (ReferenceEquals(invocation, enableProcessorInvocation))
            {
                continue;
            }

            var method = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;

            if (method is null)
            {
                continue;
            }

            if (isInbox && IsInboxDispatcherRegistration(method, compilation))
            {
                return true;
            }

            if (!isInbox && IsOutboxDispatcherRegistration(method, compilation))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Determines whether the enclosing scope enables the save-changes interceptor for transactional EF storage.
    /// </summary>
    /// <param name="enforceTransactionalInvocation">The transactional enforcement invocation.</param>
    /// <param name="semanticModel">The semantic model.</param>
    /// <returns><see langword="true" /> when the interceptor is enabled in the same scope.</returns>
    internal static bool HasSaveChangesInterceptorInScope(
        InvocationExpressionSyntax enforceTransactionalInvocation,
        SemanticModel semanticModel)
    {
        var scope = GetConfigurationScope(enforceTransactionalInvocation);

        if (scope is null)
        {
            return false;
        }

        foreach (var invocation in scope.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (ReferenceEquals(invocation, enforceTransactionalInvocation))
            {
                continue;
            }

            var method = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;

            if (method?.Name == "EnableSaveChangesInterceptor")
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Determines whether the invocation enables an inbox or outbox processor.
    /// </summary>
    /// <param name="invocation">The invocation expression syntax.</param>
    /// <param name="semanticModel">The semantic model.</param>
    /// <param name="isInbox">When <see langword="true" />, checks inbox processor enablement; otherwise, outbox.</param>
    /// <returns><see langword="true" /> when the invocation enables a processor.</returns>
    internal static bool IsProcessorEnablement(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        bool isInbox)
    {
        var method = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;

        if (method is null)
        {
            return false;
        }

        var expectedName = isInbox ? "EnableInboxProcessor" : "EnableOutboxProcessor";

        var expectedBuilder = isInbox
            ? "LiteBus.Inbox.Abstractions.InboxModuleBuilder"
            : "LiteBus.Outbox.Abstractions.OutboxModuleBuilder";

        if (method.Name != expectedName)
        {
            return false;
        }

        var containingType = method.ContainingType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        return containingType == expectedBuilder ||
               containingType == $"global::{expectedBuilder}";
    }

    /// <summary>
    ///     Determines whether the invocation enforces transactional EF storage setup.
    /// </summary>
    /// <param name="invocation">The invocation expression syntax.</param>
    /// <param name="semanticModel">The semantic model.</param>
    /// <returns><see langword="true" /> when the invocation enforces transactional setup.</returns>
    internal static bool IsTransactionalStorageEnforcement(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel)
    {
        var method = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;

        if (method?.Name != "EnforceTransactionalSetup")
        {
            return false;
        }

        var containingType = method.ContainingType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        return containingType is
            "global::LiteBus.Inbox.Storage.EntityFrameworkCore.EfCoreInboxStorageModuleBuilder" or
            "LiteBus.Inbox.Storage.EntityFrameworkCore.EfCoreInboxStorageModuleBuilder" or
            "global::LiteBus.Outbox.Storage.EntityFrameworkCore.EfCoreOutboxStorageModuleBuilder" or
            "LiteBus.Outbox.Storage.EntityFrameworkCore.EfCoreOutboxStorageModuleBuilder";
    }

    /// <summary>
    ///     Gets the configuration scope that contains a module builder callback.
    /// </summary>
    /// <param name="invocation">The configuration invocation.</param>
    /// <returns>The enclosing syntax node that bounds the configuration scope.</returns>
    private static SyntaxNode GetConfigurationScope(InvocationExpressionSyntax invocation)
    {
        for (var node = invocation.Parent; node is not null; node = node.Parent)
        {
            if (node is AnonymousFunctionExpressionSyntax or MethodDeclarationSyntax or ConstructorDeclarationSyntax)
            {
                return node;
            }
        }

        return invocation.SyntaxTree.GetRoot();
    }

    /// <summary>
    ///     Determines whether the method registers an inbox dispatcher.
    /// </summary>
    /// <param name="method">The invoked method symbol.</param>
    /// <param name="compilation">The compilation being analyzed.</param>
    /// <returns><see langword="true" /> when the method registers an inbox dispatcher.</returns>
    private static bool IsInboxDispatcherRegistration(IMethodSymbol method, Compilation compilation)
    {
        if (method.Name is not (
            "UseCommandInboxDispatcher"
            or "UseAmqpDispatch"
            or "UseInMemoryDispatch"
            or "UseAzureServiceBusDispatch"
            or "UseAwsSqsDispatch"
            or "UseKafkaDispatch"
            or "RegisterDispatcher"))
        {
            return false;
        }

        var inboxBuilder = compilation.GetTypeByMetadataName("LiteBus.Inbox.Abstractions.InboxModuleBuilder");

        return inboxBuilder is not null &&
               (SymbolEqualityComparer.Default.Equals(method.ContainingType, inboxBuilder) ||
                method.IsExtensionMethod &&
                method.Parameters.Length > 0 &&
                SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, inboxBuilder));
    }

    /// <summary>
    ///     Determines whether the method registers an outbox dispatcher.
    /// </summary>
    /// <param name="method">The invoked method symbol.</param>
    /// <param name="compilation">The compilation being analyzed.</param>
    /// <returns><see langword="true" /> when the method registers an outbox dispatcher.</returns>
    private static bool IsOutboxDispatcherRegistration(IMethodSymbol method, Compilation compilation)
    {
        if (method.Name is not (
            "UseEventOutboxDispatcher"
            or "UseAmqpDispatch"
            or "UseInMemoryDispatch"
            or "UseAzureServiceBusDispatch"
            or "UseAwsSqsDispatch"
            or "UseKafkaDispatch"
            or "RegisterDispatcher"))
        {
            return false;
        }

        var outboxBuilder = compilation.GetTypeByMetadataName("LiteBus.Outbox.Abstractions.OutboxModuleBuilder");

        return outboxBuilder is not null &&
               (SymbolEqualityComparer.Default.Equals(method.ContainingType, outboxBuilder) ||
                method.IsExtensionMethod &&
                method.Parameters.Length > 0 &&
                SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, outboxBuilder));
    }
}