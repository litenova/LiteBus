using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace LiteBus.Analyzers;

/// <summary>
///     Reports shortcuts that use the untyped shortcut contract for a message that produces a result.
/// </summary>
/// <remarks>
///     <para>
///         <c>ICommand&lt;TResult&gt;</c> derives from <c>ICommand</c>, so <c>ICommandShortcut&lt;TCommand&gt;</c>
///         compiles for a command that produces a result. It should not be used there: the untyped <c>Shortcut</c>
///         carries no result, so answering reaches the caller as <c>LiteBusConfigurationException</c> rather than as the
///         value the caller expects.
///     </para>
///     <para>
///         The guard contracts have no equivalent trap, which is why this rule covers only shortcuts. A denial does not
///         owe the caller the value the main handler would have produced, so the untyped guard is correct for every
///         message and denies by raising <c>LiteBusMessageDeniedException</c> by design.
///     </para>
///     <para>
///         The rule reports the declaration rather than an individual call, because the contract choice is the mistake
///         and the declaration is where the fix goes.
///     </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UntypedShortcutOnResultMessageAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    ///     The metadata name of the shortcut contract that carries no result type.
    /// </summary>
    private const string UntypedShortcutMetadataName = "LiteBus.Messaging.Abstractions.IMessageShortcut`1";

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
        [DiagnosticDescriptors.UntypedShortcutOnResultMessage];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(RegisterShortcutAnalysis);
    }

    /// <summary>
    ///     Registers the per-type analysis when the compilation references the shortcut and message contracts.
    /// </summary>
    /// <param name="context">The compilation start analysis context.</param>
    private static void RegisterShortcutAnalysis(CompilationStartAnalysisContext context)
    {
        var untypedShortcut = context.Compilation.GetTypeByMetadataName(UntypedShortcutMetadataName);

        if (untypedShortcut is null)
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
                untypedShortcut,
                commandWithResult,
                queryWithResult,
                streamQuery),
            SymbolKind.NamedType);
    }

    /// <summary>
    ///     Reports a shortcut type that implements the untyped contract for a message that produces a result.
    /// </summary>
    /// <param name="context">The symbol analysis context.</param>
    /// <param name="untypedShortcut">The untyped shortcut contract symbol.</param>
    /// <param name="commandWithResult">The command-with-result contract symbol, when referenced.</param>
    /// <param name="queryWithResult">The query-with-result contract symbol, when referenced.</param>
    /// <param name="streamQuery">The stream query contract symbol, when referenced.</param>
    private static void AnalyzeNamedType(
        SymbolAnalysisContext context,
        INamedTypeSymbol untypedShortcut,
        INamedTypeSymbol? commandWithResult,
        INamedTypeSymbol? queryWithResult,
        INamedTypeSymbol? streamQuery)
    {
        if (context.Symbol is not INamedTypeSymbol shortcutType || shortcutType.TypeKind == TypeKind.Interface)
        {
            return;
        }

        foreach (var shortcutInterface in shortcutType.AllInterfaces)
        {
            if (!SymbolEqualityComparer.Default.Equals(shortcutInterface.OriginalDefinition, untypedShortcut))
            {
                continue;
            }

            // An open generic shortcut declares its message through a type parameter, so the result type is unknown
            // until the runtime message type is known and nothing can be decided here.
            if (shortcutInterface.TypeArguments[0] is not INamedTypeSymbol messageType)
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
                DiagnosticDescriptors.UntypedShortcutOnResultMessage,
                shortcutType.Locations.FirstOrDefault(location => location.IsInSource)
                ?? shortcutType.Locations.FirstOrDefault()
                ?? Location.None,
                shortcutType.Name,
                messageType.Name,
                resultType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                contractName));
        }
    }

    /// <summary>
    ///     Resolves the typed shortcut contract a message that produces a result should be answered through.
    /// </summary>
    /// <param name="messageType">The message type the shortcut was declared for.</param>
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
                return ("IStreamQueryShortcut", candidate.TypeArguments[0]);
            }

            if (queryWithResult is not null && SymbolEqualityComparer.Default.Equals(definition, queryWithResult))
            {
                return ("IQueryShortcut", candidate.TypeArguments[0]);
            }

            if (commandWithResult is not null && SymbolEqualityComparer.Default.Equals(definition, commandWithResult))
            {
                return ("ICommandShortcut", candidate.TypeArguments[0]);
            }
        }

        return null;
    }
}
