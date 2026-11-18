using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using LiteBus.Analyzers.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace LiteBus.Analyzers;

/// <summary>
///     Reports definition classes that declare more than one message definition concern.
/// </summary>
/// <remarks>
///     <para>
///         Authorization, audit, tenancy and idempotency are unrelated concerns. The registry accepts one class
///         implementing several definitions, but one class per concern keeps each decision readable and reviewable on
///         its own: <c>PlaceOrderCommandAuthorizationDefinition</c> beside
///         <c>PlaceOrderCommandAuditDefinition</c> rather than one <c>PlaceOrderCommandDefinition</c> mixing both.
///     </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DefinitionMixesConcernsAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    ///     The metadata name of the open generic definition contract.
    /// </summary>
    private const string MessageDefinitionMetadataName = "LiteBus.Messaging.Abstractions.IMessageDefinition`2";

    /// <summary>
    ///     The metadata name of the authorization declaration value type.
    /// </summary>
    private const string AuthorizationDeclarationMetadataName =
        "LiteBus.Messaging.Abstractions.AuthorizationDeclaration";

    /// <summary>
    ///     The metadata name of the audit declaration value type.
    /// </summary>
    private const string AuditDeclarationMetadataName = "LiteBus.Messaging.Abstractions.Auditing.AuditDeclaration";

    /// <summary>
    ///     The metadata name of the tenancy declaration value type.
    /// </summary>
    private const string TenancyDeclarationMetadataName = "LiteBus.Messaging.Abstractions.Tenancy.TenancyDeclaration";

    /// <summary>
    ///     The metadata name of the idempotency declaration value type.
    /// </summary>
    private const string IdempotencyDeclarationMetadataName =
        "LiteBus.Messaging.Abstractions.IdempotencyDeclaration";

    /// <summary>
    ///     The suffix stripped from a custom value type name when suggesting the split definition class name.
    /// </summary>
    private const string DeclarationSuffix = "Declaration";

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        [DiagnosticDescriptors.DefinitionMixesConcerns];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(RegisterDefinitionAnalysis);
    }

    /// <summary>
    ///     Registers the per-type analysis when the compilation references the definition contract.
    /// </summary>
    /// <param name="context">The compilation start analysis context.</param>
    private static void RegisterDefinitionAnalysis(CompilationStartAnalysisContext context)
    {
        var definitionContract = context.Compilation.GetTypeByMetadataName(MessageDefinitionMetadataName);

        if (definitionContract is null)
        {
            return;
        }

        var concerns = new DefinitionConcernSymbols(
            context.Compilation.GetTypeByMetadataName(AuthorizationDeclarationMetadataName),
            context.Compilation.GetTypeByMetadataName(AuditDeclarationMetadataName),
            context.Compilation.GetTypeByMetadataName(TenancyDeclarationMetadataName),
            context.Compilation.GetTypeByMetadataName(IdempotencyDeclarationMetadataName));

        context.RegisterSymbolAction(
            symbolContext => AnalyzeNamedType(symbolContext, definitionContract, concerns),
            SymbolKind.NamedType);
    }

    /// <summary>
    ///     Reports a definition class that implements more than one message definition concern.
    /// </summary>
    /// <param name="context">The symbol analysis context.</param>
    /// <param name="definitionContract">The open generic definition contract symbol.</param>
    /// <param name="concerns">The resolved declaration value type symbols.</param>
    /// <remarks>
    ///     Only classes declared in the analyzed assembly are reported. A definition in a referenced assembly cannot be
    ///     split from here, so reporting it would produce a note with no available fix.
    /// </remarks>
    private static void AnalyzeNamedType(
        SymbolAnalysisContext context,
        INamedTypeSymbol definitionContract,
        DefinitionConcernSymbols concerns)
    {
        if (context.Symbol is not INamedTypeSymbol definitionType ||
            definitionType.TypeKind != TypeKind.Class ||
            !SymbolEqualityComparer.Default.Equals(definitionType.ContainingAssembly, context.Compilation.Assembly))
        {
            return;
        }

        var declared = CollectDeclaredValues(definitionType, definitionContract);

        if (declared.Count < 2)
        {
            return;
        }

        var ordered = declared
            .Select(entry => ResolveConcern(entry.Message, entry.Value, concerns))
            .OrderBy(concern => concern.Order)
            .ThenBy(concern => concern.DisplayName, StringComparer.Ordinal)
            .ToList();

        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.DefinitionMixesConcerns,
            LiteBusSymbols.GetDiagnosticLocation(
                context.Compilation,
                definitionType.Locations.FirstOrDefault(location => location.IsInSource) ??
                definitionType.Locations.FirstOrDefault() ??
                Location.None),
            definitionType.Name,
            JoinConcernNames(ordered),
            ordered[0].Message.Name + ordered[0].ConcernName + "Definition"));
    }

    /// <summary>
    ///     Collects the distinct definition values one class declares.
    /// </summary>
    /// <param name="definitionType">The candidate definition class.</param>
    /// <param name="definitionContract">The open generic definition contract symbol.</param>
    /// <returns>The distinct declared values with the message each was first declared for.</returns>
    /// <remarks>
    ///     The named contracts each derive from the open generic definition contract, so matching it covers
    ///     <c>IAuditDefinition</c>, <c>IAuthorizationDefinition</c>, <c>ITenancyDefinition</c>,
    ///     <c>IIdempotencyDefinition</c> and <c>IMessageDefinition</c> implemented directly. The same value declared
    ///     twice counts once: declaring audit for two messages is one concern, not a mix.
    /// </remarks>
    private static List<(INamedTypeSymbol Message, ITypeSymbol Value)> CollectDeclaredValues(
        INamedTypeSymbol definitionType,
        INamedTypeSymbol definitionContract)
    {
        List<(INamedTypeSymbol Message, ITypeSymbol Value)> declared = [];
        HashSet<ITypeSymbol> seen = new(SymbolEqualityComparer.Default);

        foreach (var contract in definitionType.AllInterfaces)
        {
            if (!SymbolEqualityComparer.Default.Equals(contract.OriginalDefinition, definitionContract) ||
                contract.TypeArguments.Length != 2)
            {
                continue;
            }

            // An open generic definition declares its message through a type parameter, so no split class name can be
            // suggested and nothing is decided here.
            if (contract.TypeArguments[0] is not INamedTypeSymbol message)
            {
                continue;
            }

            var value = contract.TypeArguments[1];

            if (seen.Add(value))
            {
                declared.Add((message, value));
            }
        }

        return declared;
    }

    /// <summary>
    ///     Resolves one declared value to the concern it names.
    /// </summary>
    /// <param name="message">The message the value was declared for.</param>
    /// <param name="value">The declared metadata value type.</param>
    /// <param name="concerns">The resolved declaration value type symbols.</param>
    /// <returns>The concern the value names.</returns>
    private static DefinitionConcern ResolveConcern(
        INamedTypeSymbol message,
        ITypeSymbol value,
        DefinitionConcernSymbols concerns)
    {
        var original = value.OriginalDefinition;

        if (concerns.Authorization is not null &&
            SymbolEqualityComparer.Default.Equals(original, concerns.Authorization))
        {
            return new DefinitionConcern(message, "authorization", "Authorization", 0);
        }

        if (concerns.Audit is not null && SymbolEqualityComparer.Default.Equals(original, concerns.Audit))
        {
            return new DefinitionConcern(message, "audit", "Audit", 1);
        }

        if (concerns.Tenancy is not null && SymbolEqualityComparer.Default.Equals(original, concerns.Tenancy))
        {
            return new DefinitionConcern(message, "tenancy", "Tenancy", 2);
        }

        if (concerns.Idempotency is not null && SymbolEqualityComparer.Default.Equals(original, concerns.Idempotency))
        {
            return new DefinitionConcern(message, "idempotency", "Idempotency", 3);
        }

        return new DefinitionConcern(message, value.Name, StripDeclarationSuffix(value.Name), 4);
    }

    /// <summary>
    ///     Joins concern display names the way the diagnostic message lists them.
    /// </summary>
    /// <param name="concerns">The concerns in report order.</param>
    /// <returns>The joined concern names.</returns>
    private static string JoinConcernNames(List<DefinitionConcern> concerns)
    {
        if (concerns.Count == 2)
        {
            return concerns[0].DisplayName + " and " + concerns[1].DisplayName;
        }

        return string.Join(", ", concerns.Take(concerns.Count - 1).Select(concern => concern.DisplayName)) +
            ", and " + concerns[concerns.Count - 1].DisplayName;
    }

    /// <summary>
    ///     Strips the declaration suffix from a custom value type name for the suggested class name.
    /// </summary>
    /// <param name="name">The value type name.</param>
    /// <returns>The concern name used in the suggested split class name.</returns>
    private static string StripDeclarationSuffix(string name)
    {
        if (name.EndsWith(DeclarationSuffix, StringComparison.Ordinal) && name.Length > DeclarationSuffix.Length)
        {
            return name.Substring(0, name.Length - DeclarationSuffix.Length);
        }

        return name;
    }

    /// <summary>
    ///     The resolved declaration value type symbols for one compilation.
    /// </summary>
    /// <param name="Authorization">The authorization declaration value type, when referenced.</param>
    /// <param name="Audit">The audit declaration value type, when referenced.</param>
    /// <param name="Tenancy">The tenancy declaration value type, when referenced.</param>
    /// <param name="Idempotency">The idempotency declaration value type, when referenced.</param>
    private sealed record DefinitionConcernSymbols(
        INamedTypeSymbol? Authorization,
        INamedTypeSymbol? Audit,
        INamedTypeSymbol? Tenancy,
        INamedTypeSymbol? Idempotency);

    /// <summary>
    ///     Describes one concern a definition class declares.
    /// </summary>
    /// <param name="Message">The message the concern was declared for.</param>
    /// <param name="DisplayName">The concern name listed in the diagnostic message.</param>
    /// <param name="ConcernName">The concern name used in the suggested split class name.</param>
    /// <param name="Order">The report order of the concern.</param>
    private sealed record DefinitionConcern(
        INamedTypeSymbol Message,
        string DisplayName,
        string ConcernName,
        int Order);
}
