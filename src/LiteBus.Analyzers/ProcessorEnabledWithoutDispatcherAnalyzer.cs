using System.Collections.Immutable;
using LiteBus.Analyzers.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace LiteBus.Analyzers;

/// <summary>
///     Reports inbox or outbox module configuration that enables a processor without registering a dispatcher.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ProcessorEnabledWithoutDispatcherAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(DiagnosticDescriptors.ProcessorEnabledWithoutDispatcher);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    /// <summary>
    ///     Reports processor enablement that omits dispatcher registration in the same configuration scope.
    /// </summary>
    /// <param name="context">The syntax node analysis context.</param>
    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not InvocationExpressionSyntax invocation)
        {
            return;
        }

        var isInbox = ModuleConfigurationAnalysis.IsProcessorEnablement(invocation, context.SemanticModel, true);

        var isOutbox = !isInbox &&
                       ModuleConfigurationAnalysis.IsProcessorEnablement(invocation, context.SemanticModel, false);

        if (!isInbox && !isOutbox)
        {
            return;
        }

        if (ModuleConfigurationAnalysis.HasDispatcherConfigurationInScope(
                invocation,
                context.SemanticModel,
                context.Compilation))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.ProcessorEnabledWithoutDispatcher,
            invocation.GetLocation(),
            isInbox ? "Inbox module configuration" : "Outbox module configuration",
            isInbox ? "Inbox" : "Outbox",
            "UseInProcessDispatch"));
    }
}