using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace LiteBus.Analyzers.Analysis;

/// <summary>
///     The well-known declaration symbols for one compilation, resolved once at compilation start.
/// </summary>
internal sealed class DeclarationSymbols
{
    /// <summary>
    ///     The metadata name of the command marker interface.
    /// </summary>
    private const string CommandMessageMetadataName = "LiteBus.Commands.Abstractions.ICommand";

    /// <summary>
    ///     The metadata name of the command-with-result marker interface.
    /// </summary>
    private const string CommandWithResultMetadataName = "LiteBus.Commands.Abstractions.ICommand`1";

    /// <summary>
    ///     The metadata name of the query marker interface.
    /// </summary>
    private const string QueryMessageMetadataName = "LiteBus.Queries.Abstractions.IQuery";

    /// <summary>
    ///     The metadata name of the query-with-result marker interface.
    /// </summary>
    private const string QueryWithResultMetadataName = "LiteBus.Queries.Abstractions.IQuery`1";

    /// <summary>
    ///     The metadata name of the stream query marker interface.
    /// </summary>
    private const string StreamQueryMessageMetadataName = "LiteBus.Queries.Abstractions.IStreamQuery`1";

    /// <summary>
    ///     The metadata name of the open generic definition contract.
    /// </summary>
    private const string MessageDefinitionMetadataName = "LiteBus.Messaging.Abstractions.IMessageDefinition`2";

    /// <summary>
    ///     The metadata name of the describing definition contract.
    /// </summary>
    private const string DescribingDefinitionMetadataName = "LiteBus.Messaging.Abstractions.IMessageDefinition`1";

    /// <summary>
    ///     The metadata name of the declaration collector passed to <c>Describe</c>.
    /// </summary>
    private const string MessageDeclarationsMetadataName = "LiteBus.Messaging.Abstractions.IMessageDeclarations";

    /// <summary>
    ///     The metadata name of the audit declaration value type.
    /// </summary>
    private const string AuditDeclarationMetadataName = "LiteBus.Messaging.Abstractions.AuditDeclaration";

    /// <summary>
    ///     The metadata name of the annotation stating which value an attribute declares.
    /// </summary>
    private const string MessageDeclarationAttributeMetadataName =
        "LiteBus.Messaging.Abstractions.MessageDeclarationAttribute";

    /// <summary>
    ///     The metadata name of the general exemption attribute.
    /// </summary>
    private const string DeclarationExemptAttributeMetadataName =
        "LiteBus.Messaging.Abstractions.DeclarationExemptAttribute";

    /// <summary>
    ///     Initializes a new instance of the <see cref="DeclarationSymbols" /> class.
    /// </summary>
    /// <param name="compilation">The compilation being analyzed.</param>
    internal DeclarationSymbols(Compilation compilation)
    {
        CommandMarkers = CollectMarkers(
            compilation.GetTypeByMetadataName(CommandMessageMetadataName),
            compilation.GetTypeByMetadataName(CommandWithResultMetadataName));
        QueryMarkers = CollectMarkers(
            compilation.GetTypeByMetadataName(QueryMessageMetadataName),
            compilation.GetTypeByMetadataName(QueryWithResultMetadataName));
        StreamQueryMarkers = CollectMarkers(
            compilation.GetTypeByMetadataName(StreamQueryMessageMetadataName));
        DefinitionContract = compilation.GetTypeByMetadataName(MessageDefinitionMetadataName);
        DescribingContract = compilation.GetTypeByMetadataName(DescribingDefinitionMetadataName);
        Declarations = compilation.GetTypeByMetadataName(MessageDeclarationsMetadataName);
        AuditDeclaration = compilation.GetTypeByMetadataName(AuditDeclarationMetadataName);
        MessageDeclarationAnnotation = compilation.GetTypeByMetadataName(MessageDeclarationAttributeMetadataName);
        DeclarationExempt = compilation.GetTypeByMetadataName(DeclarationExemptAttributeMetadataName);
    }

    /// <summary>
    ///     Gets the resolved command marker interfaces.
    /// </summary>
    internal ImmutableArray<INamedTypeSymbol> CommandMarkers { get; }

    /// <summary>
    ///     Gets the resolved query marker interfaces.
    /// </summary>
    internal ImmutableArray<INamedTypeSymbol> QueryMarkers { get; }

    /// <summary>
    ///     Gets the resolved stream query marker interfaces.
    /// </summary>
    internal ImmutableArray<INamedTypeSymbol> StreamQueryMarkers { get; }

    /// <summary>
    ///     Gets the open generic definition contract, when referenced.
    /// </summary>
    internal INamedTypeSymbol? DefinitionContract { get; }

    /// <summary>
    ///     Gets the describing definition contract, when referenced.
    /// </summary>
    internal INamedTypeSymbol? DescribingContract { get; }

    /// <summary>
    ///     Gets the declaration collector passed to <c>Describe</c>, when referenced.
    /// </summary>
    internal INamedTypeSymbol? Declarations { get; }

    /// <summary>
    ///     Gets the audit declaration value type, when referenced.
    /// </summary>
    internal INamedTypeSymbol? AuditDeclaration { get; }

    /// <summary>
    ///     Gets the annotation stating which value an attribute declares, when referenced.
    /// </summary>
    internal INamedTypeSymbol? MessageDeclarationAnnotation { get; }

    /// <summary>
    ///     Gets the general exemption attribute, when referenced.
    /// </summary>
    internal INamedTypeSymbol? DeclarationExempt { get; }

    /// <summary>
    ///     Gets a value indicating whether any message marker resolved.
    /// </summary>
    internal bool HasMessageMarkers =>
        !CommandMarkers.IsEmpty || !QueryMarkers.IsEmpty || !StreamQueryMarkers.IsEmpty;

    /// <summary>
    ///     Gets the resolved markers for one message kind.
    /// </summary>
    /// <param name="kind">The message kind.</param>
    /// <returns>The resolved marker interfaces.</returns>
    internal ImmutableArray<INamedTypeSymbol> MarkersFor(MessageKind kind)
    {
        return kind switch
        {
            MessageKind.Command => CommandMarkers,
            MessageKind.Query => QueryMarkers,
            MessageKind.StreamQuery => StreamQueryMarkers,
            _ => QueryMarkers
        };
    }

    /// <summary>
    ///     Collects the marker interfaces that resolved.
    /// </summary>
    /// <param name="candidates">The resolution results.</param>
    /// <returns>The resolved markers.</returns>
    private static ImmutableArray<INamedTypeSymbol> CollectMarkers(params INamedTypeSymbol?[] candidates)
    {
        var builder = ImmutableArray.CreateBuilder<INamedTypeSymbol>();

        foreach (var candidate in candidates)
        {
            if (candidate is not null)
            {
                builder.Add(candidate);
            }
        }

        return builder.ToImmutable();
    }
}
