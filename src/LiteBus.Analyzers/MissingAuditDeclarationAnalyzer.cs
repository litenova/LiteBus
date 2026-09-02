using System.Collections.Immutable;
using LiteBus.Analyzers.Analysis;
using Microsoft.CodeAnalysis;
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
    /// <summary>
    ///     The metadata name of the audit declaration value type.
    /// </summary>
    private const string AuditDeclarationMetadataName = "LiteBus.Messaging.Abstractions.AuditDeclaration";

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        [DiagnosticDescriptors.MissingAuditDeclaration];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationAction(AnalyzeCompilation);
    }

    /// <summary>
    ///     Reports command and query types that declare no audit position.
    /// </summary>
    /// <param name="context">The compilation analysis context.</param>
    private static void AnalyzeCompilation(CompilationAnalysisContext context)
    {
        var auditDeclaration = context.Compilation.GetTypeByMetadataName(AuditDeclarationMetadataName);

        if (auditDeclaration is null)
        {
            // The audit contracts are not referenced, so the codebase has not opted into auditing at all.
            return;
        }

        foreach (var message in DeclarationAnalysis.CollectUndeclared(context, auditDeclaration))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.MissingAuditDeclaration,
                LiteBusSymbols.GetDiagnosticLocation(context.Compilation, message.Location),
                message.MessageType.Name));
        }
    }
}
