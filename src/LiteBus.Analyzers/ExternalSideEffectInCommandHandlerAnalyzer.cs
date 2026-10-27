using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using LiteBus.Analyzers.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace LiteBus.Analyzers;

/// <summary>
///     Reports command handlers that inject external side-effect types through their constructor.
/// </summary>
/// <remarks>
///     <para>
///         A command handler that acts on the outside world, an HTTP API, a file share, or a device, before the
///         unit of work commits leaves effects the commit cannot roll back. The call belongs behind the
///         transactional outbox or in a completion handler that runs after the commit.
///     </para>
///     <para>
///         The default forbidden types are <c>System.Net.Http.HttpClient</c> and
///         <c>System.Net.Http.IHttpClientFactory</c>. Set <c>litebus.LB1024.forbidden_types</c> in
///         <c>.editorconfig</c> to a comma-separated list of metadata names to replace them; a configured value
///         that names nothing reports nothing. Names that do not resolve are ignored.
///     </para>
///     <para>
///         Disabled by default. Enable with <c>dotnet_diagnostic.LB1024.severity = warning</c> once the codebase is
///         ready to keep outside-world calls out of command handlers.
///     </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ExternalSideEffectInCommandHandlerAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    ///     The <c>.editorconfig</c> key naming the forbidden side-effect types.
    /// </summary>
    internal const string ForbiddenTypesOption = "litebus.LB1024.forbidden_types";

    /// <summary>
    ///     The forbidden side-effect types used when the option is not configured.
    /// </summary>
    private static readonly string[] DefaultForbiddenTypeMetadataNames =
    [
        "System.Net.Http.HttpClient",
        "System.Net.Http.IHttpClientFactory"
    ];

    /// <summary>
    ///     The separators accepted between type names in the configured list.
    /// </summary>
    private static readonly char[] NameSeparators = [',', ';'];

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        [DiagnosticDescriptors.ExternalSideEffectInCommandHandler];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
    }

    /// <summary>
    ///     Reports constructor parameters whose type is a forbidden side-effect type.
    /// </summary>
    /// <param name="context">The symbol analysis context.</param>
    private static void AnalyzeNamedType(SymbolAnalysisContext context)
    {
        if (context.Symbol is not INamedTypeSymbol handlerType ||
            !SymbolEqualityComparer.Default.Equals(handlerType.ContainingAssembly, context.Compilation.Assembly))
        {
            return;
        }

        if (!CommandHandlerAnalysis.IsCommandHandler(handlerType, context.Compilation))
        {
            return;
        }

        foreach (var constructor in handlerType.InstanceConstructors)
        {
            var forbiddenTypes = GetForbiddenTypes(context, constructor);

            if (forbiddenTypes.Count == 0)
            {
                continue;
            }

            foreach (var parameter in constructor.Parameters)
            {
                ReportIfForbidden(context, handlerType, parameter, forbiddenTypes);
            }
        }
    }

    /// <summary>
    ///     Gets the forbidden side-effect type names configured for the constructor's syntax tree.
    /// </summary>
    /// <param name="context">The symbol analysis context.</param>
    /// <param name="constructor">The constructor symbol.</param>
    /// <returns>The configured metadata names, or the defaults when nothing is configured.</returns>
    private static IReadOnlyList<string> GetForbiddenTypes(SymbolAnalysisContext context, IMethodSymbol constructor)
    {
        var tree = constructor.DeclaringSyntaxReferences.FirstOrDefault()?.SyntaxTree;

        if (tree is not null &&
            context.Options.AnalyzerConfigOptionsProvider.GetOptions(tree).TryGetValue(ForbiddenTypesOption, out var treeValue) &&
            treeValue is not null)
        {
            return SplitConfiguredNames(treeValue);
        }

        if (context.Options.AnalyzerConfigOptionsProvider.GlobalOptions.TryGetValue(ForbiddenTypesOption, out var globalValue) &&
            globalValue is not null)
        {
            return SplitConfiguredNames(globalValue);
        }

        return DefaultForbiddenTypeMetadataNames;
    }

    /// <summary>
    ///     Splits one configured value into metadata names.
    /// </summary>
    /// <param name="value">The raw configuration value.</param>
    /// <returns>The trimmed, non-empty names in configuration order.</returns>
    private static List<string> SplitConfiguredNames(string value)
    {
        List<string> names = [];

        foreach (var part in value.Split(NameSeparators, StringSplitOptions.RemoveEmptyEntries))
        {
            var name = part.Trim();

            if (name.Length > 0)
            {
                names.Add(name);
            }
        }

        return names;
    }

    /// <summary>
    ///     Reports a diagnostic when the parameter type is a forbidden side-effect type.
    /// </summary>
    /// <param name="context">The symbol analysis context.</param>
    /// <param name="handlerType">The command handler type symbol.</param>
    /// <param name="parameter">The constructor parameter symbol.</param>
    /// <param name="forbiddenTypes">The forbidden side-effect type metadata names.</param>
    private static void ReportIfForbidden(
        SymbolAnalysisContext context,
        INamedTypeSymbol handlerType,
        IParameterSymbol parameter,
        IReadOnlyList<string> forbiddenTypes)
    {
        var forbiddenName = GetForbiddenMetadataName(parameter.Type, forbiddenTypes, context.Compilation);

        if (forbiddenName is null)
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.ExternalSideEffectInCommandHandler,
            LiteBusSymbols.GetDiagnosticLocation(
                context.Compilation,
                parameter.Locations.FirstOrDefault() ?? Location.None),
            handlerType.Name,
            forbiddenName));
    }

    /// <summary>
    ///     Gets the forbidden metadata name matched by the parameter type, if any.
    /// </summary>
    /// <param name="parameterType">The parameter type symbol.</param>
    /// <param name="forbiddenTypes">The forbidden side-effect type metadata names.</param>
    /// <param name="compilation">The compilation being analyzed.</param>
    /// <returns>The matched metadata name, if the parameter type is forbidden.</returns>
    private static string? GetForbiddenMetadataName(
        ITypeSymbol parameterType,
        IReadOnlyList<string> forbiddenTypes,
        Compilation compilation)
    {
        foreach (var metadataName in forbiddenTypes)
        {
            var expected = compilation.GetTypeByMetadataName(metadataName);

            if (expected is null)
            {
                continue;
            }

            if (IsForbiddenType(parameterType, expected))
            {
                return metadataName;
            }
        }

        return null;
    }

    /// <summary>
    ///     Determines whether the parameter type is the forbidden type, derives from it, or implements it.
    /// </summary>
    /// <param name="parameterType">The parameter type symbol.</param>
    /// <param name="forbidden">The forbidden type symbol.</param>
    /// <returns><see langword="true" /> when the parameter type is forbidden; otherwise, <see langword="false" />.</returns>
    private static bool IsForbiddenType(ITypeSymbol parameterType, INamedTypeSymbol forbidden)
    {
        for (var current = parameterType; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, forbidden))
            {
                return true;
            }
        }

        foreach (var candidate in parameterType.AllInterfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, forbidden))
            {
                return true;
            }
        }

        return false;
    }
}
