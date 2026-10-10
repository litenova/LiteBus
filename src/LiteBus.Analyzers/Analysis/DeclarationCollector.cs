using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace LiteBus.Analyzers.Analysis;

/// <summary>
///     Collects declaration facts gathered by symbol, syntax node, and syntax tree actions for one compilation.
/// </summary>
/// <remarks>
///     Actions run concurrently and in no defined order, so every fact bag is a concurrent collection. The
///     compilation-end action sorts and evaluates the facts.
/// </remarks>
internal sealed class DeclarationCollector
{
    /// <summary>
    ///     Gets the command message candidates declared in the analyzed assembly.
    /// </summary>
    internal ConcurrentBag<MessageTypeRegistration> Commands { get; } = new();

    /// <summary>
    ///     Gets the query message candidates declared in the analyzed assembly.
    /// </summary>
    internal ConcurrentBag<MessageTypeRegistration> Queries { get; } = new();

    /// <summary>
    ///     Gets the stream query message candidates declared in the analyzed assembly.
    /// </summary>
    internal ConcurrentBag<MessageTypeRegistration> StreamQueries { get; } = new();

    /// <summary>
    ///     Gets the keyed definitions, each pairing the described message type with the declared value type.
    /// </summary>
    internal ConcurrentBag<(INamedTypeSymbol Described, ITypeSymbol Value)> KeyedDefinitions { get; } = new();

    /// <summary>
    ///     Gets the describing definitions, each pairing the definition type with the described message type.
    /// </summary>
    internal ConcurrentBag<(INamedTypeSymbol Definition, INamedTypeSymbol Described)> DescribingDefinitions { get; } =
        new();

    /// <summary>
    ///     Gets the recorded <c>Describe</c> bodies, each pairing the method with what the body declares.
    /// </summary>
    internal ConcurrentBag<(
        IMethodSymbol Method,
        ImmutableArray<ITypeSymbol> DeclaredValues,
        bool StatesAuditPosition,
        bool PassesCollector,
        SyntaxTree Tree,
        TextSpan Span)> DescribeBodies { get; } = new();

    /// <summary>
    ///     Gets the source attribute classes annotated as declaring a value type.
    /// </summary>
    internal ConcurrentBag<(INamedTypeSymbol AttributeClass, INamedTypeSymbol DeclaredValue)>
        SourceDeclaringAttributes { get; } = new();

    /// <summary>
    ///     Gets the required declaration names configured per syntax tree.
    /// </summary>
    internal ConcurrentBag<(SyntaxTree Tree, string[] Names)> TreeOptions { get; } = new();

    /// <summary>
    ///     Gets the message candidates for one message kind.
    /// </summary>
    /// <param name="kind">The message kind.</param>
    /// <returns>The candidates collected for that kind.</returns>
    internal ConcurrentBag<MessageTypeRegistration> Messages(MessageKind kind)
    {
        return kind switch
        {
            MessageKind.Command => Commands,
            MessageKind.Query => Queries,
            MessageKind.StreamQuery => StreamQueries,
            _ => Queries
        };
    }
}
