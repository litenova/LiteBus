using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using LiteBus.Analyzers.Analysis;
using Microsoft.CodeAnalysis;
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
    /// <summary>
    ///     The <c>.editorconfig</c> key naming the metadata value types every message must declare.
    /// </summary>
    private const string RequiredDeclarationsOption = "litebus_required_declarations";

    /// <summary>
    ///     The separators accepted between value type names in the configured list.
    /// </summary>
    private static readonly char[] NameSeparators = [',', ';'];

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
    [
        DiagnosticDescriptors.MissingDeclaration,
        DiagnosticDescriptors.UnresolvedRequiredDeclaration
    ];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationAction(AnalyzeCompilation);
    }

    /// <summary>
    ///     Reports messages that state no position on each configured value type.
    /// </summary>
    /// <param name="context">The compilation analysis context.</param>
    private static void AnalyzeCompilation(CompilationAnalysisContext context)
    {
        var configured = ReadRequiredDeclarations(context);

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

            foreach (var message in DeclarationAnalysis.CollectUndeclared(context, valueType))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.MissingDeclaration,
                    LiteBusSymbols.GetDiagnosticLocation(context.Compilation, message.Location),
                    message.MessageType.Name,
                    valueType.Name));
            }
        }
    }

    /// <summary>
    ///     Reads the configured value type names from analyzer configuration.
    /// </summary>
    /// <param name="context">The compilation analysis context.</param>
    /// <returns>The configured metadata names, deduplicated and in configuration order.</returns>
    /// <remarks>
    ///     Global options come from a <c>.globalconfig</c> or a compiler-visible MSBuild property, while an
    ///     <c>.editorconfig</c> entry under <c>[*.cs]</c> is attached to each syntax tree. Both are read, because a
    ///     project-wide rule is natural to write in either place and a rule that silently ignores one of them looks
    ///     broken.
    /// </remarks>
    private static List<string> ReadRequiredDeclarations(CompilationAnalysisContext context)
    {
        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        if (context.Options.AnalyzerConfigOptionsProvider.GlobalOptions
            .TryGetValue(RequiredDeclarationsOption, out var globalValue))
        {
            AddNames(globalValue, names, seen);
        }

        foreach (var syntaxTree in context.Compilation.SyntaxTrees)
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            if (context.Options.AnalyzerConfigOptionsProvider.GetOptions(syntaxTree)
                .TryGetValue(RequiredDeclarationsOption, out var treeValue))
            {
                AddNames(treeValue, names, seen);
            }
        }

        return names;
    }

    /// <summary>
    ///     Splits one configured value into metadata names and adds the ones not already collected.
    /// </summary>
    /// <param name="value">The raw configuration value.</param>
    /// <param name="names">The collected names.</param>
    /// <param name="seen">The names already collected.</param>
    private static void AddNames(string value, List<string> names, HashSet<string> seen)
    {
        foreach (var part in value.Split(NameSeparators, StringSplitOptions.RemoveEmptyEntries))
        {
            var name = part.Trim();

            if (name.Length > 0 && seen.Add(name))
            {
                names.Add(name);
            }
        }
    }
}
