using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace LiteBus.Analyzers;

/// <summary>
///     Reports gates that use the untyped gate contract for a message that produces a result.
/// </summary>
/// <remarks>
///     <para>
///         <c>ICommand&lt;TResult&gt;</c> derives from <c>ICommand</c>, so <c>ICommandGate&lt;TCommand&gt;</c> compiles
///         for a command that produces a result. It should not be used there: the untyped <c>PipelineDirective</c>
///         carries no result, so a short-circuit reaches the caller as <c>LiteBusConfigurationException</c> rather than
///         as the value the caller expects.
///     </para>
///     <para>
///         The typed contract is a strict superset of the untyped one for such a message. It can continue, refuse with a
///         result, refuse without one, and short-circuit, so there is no decision the untyped contract expresses that
///         the typed contract cannot. That is why this reports the declaration rather than an individual call: the
///         contract choice is the mistake, and the declaration is where the fix goes.
///     </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UntypedGateOnResultMessageAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    ///     The metadata name of the gate contract that carries no result type.
    /// </summary>
    private const string UntypedGateMetadataName = "LiteBus.Messaging.Abstractions.IMessageGate`1";

    /// <summary>
    ///     The metadata name of the command contract that declares a result type.
    /// </summary>
    private const string CommandWithResultMetadataName = "LiteBus.Commands.Abstractions.ICommand`1";

    /// <summary>
    ///     The metadata name of the query contract that declares a result type.
    /// </summary>
    private const string QueryWithResultMetadataName = "LiteBus.Queries.Abstractions.IQuery`1";

    /// <summary>
    ///     The metadata name of the stream query contract that declares an item type.
    /// </summary>
    private const string StreamQueryMetadataName = "LiteBus.Queries.Abstractions.IStreamQuery`1";

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        [DiagnosticDescriptors.UntypedGateOnResultMessage];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(RegisterGateAnalysis);
    }

    /// <summary>
    ///     Registers the per-type analysis when the compilation references the gate and message contracts.
    /// </summary>
    /// <param name="context">The compilation start analysis context.</param>
    private static void RegisterGateAnalysis(CompilationStartAnalysisContext context)
    {
        var untypedGate = context.Compilation.GetTypeByMetadataName(UntypedGateMetadataName);

        if (untypedGate is null)
        {
            return;
        }

        var commandWithResult = context.Compilation.GetTypeByMetadataName(CommandWithResultMetadataName);
        var queryWithResult = context.Compilation.GetTypeByMetadataName(QueryWithResultMetadataName);
        var streamQuery = context.Compilation.GetTypeByMetadataName(StreamQueryMetadataName);

        if (commandWithResult is null && queryWithResult is null && streamQuery is null)
        {
            return;
        }

        context.RegisterSymbolAction(
            symbolContext => AnalyzeNamedType(
                symbolContext,
                untypedGate,
                commandWithResult,
                queryWithResult,
                streamQuery),
            SymbolKind.NamedType);
    }

    /// <summary>
    ///     Reports a gate type that implements the untyped contract for a message that produces a result.
    /// </summary>
    /// <param name="context">The symbol analysis context.</param>
    /// <param name="untypedGate">The untyped gate contract symbol.</param>
    /// <param name="commandWithResult">The command-with-result contract symbol, when referenced.</param>
    /// <param name="queryWithResult">The query-with-result contract symbol, when referenced.</param>
    /// <param name="streamQuery">The stream query contract symbol, when referenced.</param>
    private static void AnalyzeNamedType(
        SymbolAnalysisContext context,
        INamedTypeSymbol untypedGate,
        INamedTypeSymbol? commandWithResult,
        INamedTypeSymbol? queryWithResult,
        INamedTypeSymbol? streamQuery)
    {
        if (context.Symbol is not INamedTypeSymbol gateType || gateType.TypeKind == TypeKind.Interface)
        {
            return;
        }

        foreach (var gateInterface in gateType.AllInterfaces)
        {
            if (!SymbolEqualityComparer.Default.Equals(gateInterface.OriginalDefinition, untypedGate))
            {
                continue;
            }

            // An open generic gate declares its message through a type parameter, so the result type is unknown until
            // the runtime message type is known and nothing can be decided here.
            if (gateInterface.TypeArguments[0] is not INamedTypeSymbol messageType)
            {
                continue;
            }

            var suggestion = ResolveTypedContract(messageType, commandWithResult, queryWithResult, streamQuery);

            if (suggestion is null)
            {
                continue;
            }

            var (contractName, resultType) = suggestion.Value;

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.UntypedGateOnResultMessage,
                gateType.Locations.FirstOrDefault(location => location.IsInSource)
                ?? gateType.Locations.FirstOrDefault()
                ?? Location.None,
                gateType.Name,
                messageType.Name,
                resultType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                contractName));
        }
    }

    /// <summary>
    ///     Resolves the typed gate contract a message that produces a result should be gated through.
    /// </summary>
    /// <param name="messageType">The message type the gate was declared for.</param>
    /// <param name="commandWithResult">The command-with-result contract symbol, when referenced.</param>
    /// <param name="queryWithResult">The query-with-result contract symbol, when referenced.</param>
    /// <param name="streamQuery">The stream query contract symbol, when referenced.</param>
    /// <returns>
    ///     The name of the contract to implement and the result type it is typed over, or <see langword="null" /> when
    ///     the message produces no result and the untyped contract is the right one.
    /// </returns>
    /// <remarks>
    ///     A stream query is matched first. <c>IStreamQuery&lt;TResult&gt;</c> derives from the non-generic
    ///     <c>IQuery</c> rather than from <c>IQuery&lt;TResult&gt;</c>, so the two are disjoint today, but the order
    ///     keeps the more specific axis in front of the more general one if that ever changes.
    /// </remarks>
    private static (string ContractName, ITypeSymbol ResultType)? ResolveTypedContract(
        INamedTypeSymbol messageType,
        INamedTypeSymbol? commandWithResult,
        INamedTypeSymbol? queryWithResult,
        INamedTypeSymbol? streamQuery)
    {
        foreach (var candidate in messageType.AllInterfaces)
        {
            var definition = candidate.OriginalDefinition;

            if (streamQuery is not null && SymbolEqualityComparer.Default.Equals(definition, streamQuery))
            {
                return ("IStreamQueryGate", candidate.TypeArguments[0]);
            }

            if (queryWithResult is not null && SymbolEqualityComparer.Default.Equals(definition, queryWithResult))
            {
                return ("IQueryGate", candidate.TypeArguments[0]);
            }

            if (commandWithResult is not null && SymbolEqualityComparer.Default.Equals(definition, commandWithResult))
            {
                return ("ICommandGate", candidate.TypeArguments[0]);
            }
        }

        return null;
    }
}
