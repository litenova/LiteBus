using System.Collections.Immutable;
using LiteBus.Analyzers.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace LiteBus.Analyzers;

/// <summary>
///     Reports command and query types that state no audit position.
/// </summary>
/// <remarks>
///     <para>
///         Auditing standards ask for the selection of audited events to be documented along with its rationale. A
///         message that carries neither <c>[Audited]</c> nor <c>[AuditExempt]</c> and has no audit definition is
///         indistinguishable from one nobody considered, which is exactly what the requirement exists to prevent.
///     </para>
///     <para>
///         This is the preconfigured instance of the general rule: it asks <see cref="DeclarationAnalysis" /> the same
///         question <c>LB1020</c> asks, with <c>AuditDeclaration</c> as the required value type. An application
///         requiring its own declarations configures <c>LB1020</c> rather than reimplementing this.
///     </para>
///     <para>
///         The rule is disabled by default, because enabling it silently would break every existing compilation. Turn it
///         on with <c>dotnet_diagnostic.LB1018.severity = warning</c> in <c>.editorconfig</c> once the codebase declares
///         its position.
///     </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MissingAuditDeclarationAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        [DiagnosticDescriptors.MissingAuditDeclaration];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(AnalyzeCompilationStart);
    }

    /// <summary>
    ///     Registers declaration fact collection when the audit and message contracts are referenced.
    /// </summary>
    /// <param name="context">The compilation start analysis context.</param>
    private static void AnalyzeCompilationStart(CompilationStartAnalysisContext context)
    {
        var symbols = new DeclarationSymbols(context.Compilation);
        var auditDeclaration = symbols.AuditDeclaration;

        if (auditDeclaration is null)
        {
            // The audit contracts are not referenced, so the codebase has not opted into auditing at all.
            return;
        }

        if (!symbols.HasMessageMarkers)
        {
            // No message contracts are referenced, so there is nothing this rule could report.
            return;
        }

        var collector = new DeclarationCollector();

        context.RegisterSymbolAction(
            symbolContext => DeclarationAnalysis.AnalyzeNamedType(symbolContext, collector, symbols),
            SymbolKind.NamedType);

        if (symbols.Declarations is not null)
        {
            context.RegisterSyntaxNodeAction(
                nodeContext => DeclarationAnalysis.AnalyzeDescribeMethod(nodeContext, collector, symbols),
                SyntaxKind.MethodDeclaration);
        }

        context.RegisterCompilationEndAction(
            endContext => ReportUndeclared(endContext, collector, symbols, auditDeclaration));
    }

    /// <summary>
    ///     Reports command and query types that declare no audit position.
    /// </summary>
    /// <param name="context">The compilation analysis context.</param>
    /// <param name="collector">The collector holding the compilation's facts.</param>
    /// <param name="symbols">The resolved declaration symbols.</param>
    /// <param name="auditDeclaration">The audit declaration value type.</param>
    private static void ReportUndeclared(
        CompilationAnalysisContext context,
        DeclarationCollector collector,
        DeclarationSymbols symbols,
        INamedTypeSymbol auditDeclaration)
    {
        foreach (var message in DeclarationAnalysis.CollectUndeclared(
                     context.Compilation,
                     collector,
                     symbols,
                     auditDeclaration,
                     context.CancellationToken))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.MissingAuditDeclaration,
                LiteBusSymbols.GetDiagnosticLocation(context.Compilation, message.Location),
                message.MessageType.Name));
        }
    }
}
