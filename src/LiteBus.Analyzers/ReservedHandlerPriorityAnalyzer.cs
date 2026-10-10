using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace LiteBus.Analyzers;

/// <summary>
///     Reports a handler whose <c>[HandlerPriority]</c> value falls inside the range LiteBus reserves for the handlers
///     it ships.
/// </summary>
/// <remarks>
///     <para>
///         The reserved window exists so an application can position its handlers relative to LiteBus stages such as
///         the persistence writer and the audit writer. A value inside the window orders the handler against those
///         stages instead, which is almost never what the author meant, and the positions inside the window may be
///         reordered between releases.
///     </para>
///     <para>
///         The mistake is easy to make and silent: reaching for <c>HandlerPriorities.Persistence</c> to order two
///         application handlers against each other compiles and runs, and produces an ordering that has nothing to do
///         with the sibling it was meant to be ordered against.
///     </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ReservedHandlerPriorityAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    ///     The lowest priority reserved by LiteBus, mirroring <c>HandlerPriorities.ReservedFloor</c>.
    /// </summary>
    private const int ReservedFloor = 1_000_000;

    /// <summary>
    ///     The first priority above the reserved range, mirroring <c>HandlerPriorities.ReservedCeiling</c>.
    /// </summary>
    private const int ReservedCeiling = 2_000_000;

    /// <summary>
    ///     The metadata name of the attribute that carries a handler's priority.
    /// </summary>
    private const string HandlerPriorityAttribute = "LiteBus.Messaging.Abstractions.HandlerPriorityAttribute";

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(DiagnosticDescriptors.ReservedHandlerPriority);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(static compilationStart =>
        {
            var attributeSymbol = compilationStart.Compilation.GetTypeByMetadataName(HandlerPriorityAttribute);

            if (attributeSymbol is null)
            {
                return;
            }

            compilationStart.RegisterSymbolAction(
                symbolContext => AnalyzeType(symbolContext, attributeSymbol),
                SymbolKind.NamedType);
        });
    }

    /// <summary>
    ///     Reports a type whose handler priority falls inside the reserved range.
    /// </summary>
    /// <param name="context">The symbol analysis context.</param>
    /// <param name="attributeSymbol">The resolved <c>HandlerPriorityAttribute</c> symbol.</param>
    private static void AnalyzeType(SymbolAnalysisContext context, INamedTypeSymbol attributeSymbol)
    {
        var type = (INamedTypeSymbol) context.Symbol;

        var attribute = type
            .GetAttributes()
            .FirstOrDefault(candidate => SymbolEqualityComparer.Default.Equals(candidate.AttributeClass, attributeSymbol));

        if (attribute is null || attribute.ConstructorArguments.Length == 0)
        {
            return;
        }

        if (attribute.ConstructorArguments[0].Value is not int priority)
        {
            return;
        }

        if (priority < ReservedFloor || priority >= ReservedCeiling)
        {
            return;
        }

        var location = attribute.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation()
                       ?? type.Locations.FirstOrDefault();

        if (location is null)
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.ReservedHandlerPriority,
            location,
            type.Name,
            priority));
    }
}
