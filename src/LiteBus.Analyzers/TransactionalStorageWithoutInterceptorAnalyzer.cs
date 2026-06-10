using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using LiteBus.Analyzers.Analysis;

namespace LiteBus.Analyzers;

/// <summary>
///     Reports transactional EF storage configuration that omits the save-changes interceptor.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TransactionalStorageWithoutInterceptorAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(DiagnosticDescriptors.TransactionalStorageWithoutInterceptor);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    /// <summary>
    ///     Reports transactional EF storage configuration that omits the save-changes interceptor.
    /// </summary>
    /// <param name="context">The syntax node analysis context.</param>
    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not InvocationExpressionSyntax invocation)
        {
            return;
        }

        if (!ModuleConfigurationAnalysis.IsTransactionalStorageEnforcement(invocation, context.SemanticModel))
        {
            return;
        }

        if (ModuleConfigurationAnalysis.HasSaveChangesInterceptorInScope(invocation, context.SemanticModel))
        {
            return;
        }

        var method = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol as IMethodSymbol;
        var containingType = method?.ContainingType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var axis = containingType is
            "global::LiteBus.Inbox.Storage.EntityFrameworkCore.EfCoreInboxStorageModuleBuilder" or
            "LiteBus.Inbox.Storage.EntityFrameworkCore.EfCoreInboxStorageModuleBuilder"
            ? "Inbox EF Core storage configuration"
            : "Outbox EF Core storage configuration";

        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.TransactionalStorageWithoutInterceptor,
            invocation.GetLocation(),
            axis));
    }
}
