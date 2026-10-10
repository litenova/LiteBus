using System.Collections.Immutable;
using LiteBus.Analyzers.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace LiteBus.Analyzers;

/// <summary>
///     Reports command and query types that state no position on a metadata value type the project requires.
/// </summary>
/// <remarks>
///     <para>
///         This is the general form of <c>LB1018</c>. Auditing was the first cross-cutting declaration worth enforcing
///         at compile time, but it is not the only one: a required permission, a tenancy scope, a retention class and
///         an idempotency key are all facts a message either states or silently omits, and an application should be
///         able to make the omission a build failure without writing its own reflection test.
///     </para>
///     <para>
///         Name the value types in <c>.editorconfig</c> and enable the rule:
///     </para>
///     <code>
/// [*.cs]
/// litebus_required_declarations = Entro.Security.RequiredPermission, Entro.Compliance.RetentionClass
/// dotnet_diagnostic.LB1020.severity = warning
///     </code>
///     <para>
///         A message satisfies the requirement with a definition class declaring the value, with an attribute annotated
///         <c>[MessageDeclaration(typeof(TValue))]</c>, or with <c>[DeclarationExempt(typeof(TValue), "rationale")]</c>.
///         A declaration written for a base type or a marker interface covers the messages beneath it.
///     </para>
///     <para>
///         <c>RequireDeclaration&lt;TValue&gt;()</c> on the messaging module enforces the same rule at composition
///         time. Use both: this reports the omission where it can be fixed, and the composition check covers messages
///         registered from an assembly this compilation never saw.
///     </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MissingDeclarationAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
    [
        DiagnosticDescriptors.MissingDeclaration,
        DiagnosticDescriptors.UnresolvedRequiredDeclaration
    ];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(AnalyzeCompilationStart);
    }

    /// <summary>
    ///     Registers declaration fact and option collection for the compilation.
    /// </summary>
    /// <param name="context">The compilation start analysis context.</param>
    /// <remarks>
    ///     The syntax tree and compilation-end actions are always registered: an unresolvable configured name is
    ///     reported even when the compilation references no LiteBus contracts at all.
    /// </remarks>
    private static void AnalyzeCompilationStart(CompilationStartAnalysisContext context)
    {
        var symbols = new DeclarationSymbols(context.Compilation);
        var collector = new DeclarationCollector();
        string[] globalNames = [];

        if (context.Options.AnalyzerConfigOptionsProvider.GlobalOptions
            .TryGetValue(DeclarationAnalysis.RequiredDeclarationsOption, out var globalValue))
        {
            globalNames = DeclarationAnalysis.SplitConfiguredNames(globalValue);
        }

        if (symbols.HasMessageMarkers)
        {
            context.RegisterSymbolAction(
                symbolContext => DeclarationAnalysis.AnalyzeNamedType(symbolContext, collector, symbols),
                SymbolKind.NamedType);

            if (symbols.Declarations is not null)
            {
                context.RegisterSyntaxNodeAction(
                    nodeContext => DeclarationAnalysis.AnalyzeDescribeMethod(nodeContext, collector, symbols),
                    SyntaxKind.MethodDeclaration);
            }
        }

        context.RegisterSyntaxTreeAction(treeContext => DeclarationAnalysis.CollectTreeOptions(treeContext, collector));

        context.RegisterCompilationEndAction(
            endContext => ReportUndeclared(endContext, collector, symbols, globalNames));
    }

    /// <summary>
    ///     Reports messages that state no position on each configured value type.
    /// </summary>
    /// <param name="context">The compilation analysis context.</param>
    /// <param name="collector">The collector holding the compilation's facts.</param>
    /// <param name="symbols">The resolved declaration symbols.</param>
    /// <param name="globalNames">The required declaration names from the global options.</param>
    private static void ReportUndeclared(
        CompilationAnalysisContext context,
        DeclarationCollector collector,
        DeclarationSymbols symbols,
        string[] globalNames)
    {
        var configured = DeclarationAnalysis.MergeConfiguredNames(globalNames, collector, context.Compilation);

        if (configured.Count == 0)
        {
            return;
        }

        foreach (var name in configured)
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            var valueType = context.Compilation.GetTypeByMetadataName(name);

            if (valueType is null)
            {
                // Skipping an unresolvable name would disable the requirement it configures, and a typo in
                // .editorconfig is exactly how that happens.
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.UnresolvedRequiredDeclaration,
                    Location.None,
                    name));
                continue;
            }

            foreach (var message in DeclarationAnalysis.CollectUndeclared(
                         context.Compilation,
                         collector,
                         symbols,
                         valueType,
                         context.CancellationToken))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.MissingDeclaration,
                    LiteBusSymbols.GetDiagnosticLocation(context.Compilation, message.Location),
                    message.MessageType.Name,
                    valueType.Name));
            }
        }
    }
}
