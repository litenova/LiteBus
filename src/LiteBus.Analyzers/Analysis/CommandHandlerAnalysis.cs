using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace LiteBus.Analyzers.Analysis;

/// <summary>
///     Shared command handler discovery for the command handler discipline rules.
/// </summary>
/// <remarks>
///     <para>
///         Both LB1023 and LB1024 apply to command handlers: types implementing
///         <c>ICommandHandler&lt;TCommand&gt;</c> or <c>ICommandHandler&lt;TCommand, TCommandResult&gt;</c>, and
///         messaging-level main handlers (<c>IAsyncMessageHandler&lt;TMessage&gt;</c>,
///         <c>IAsyncMessageHandler&lt;TMessage, TMessageResult&gt;</c>, or
///         <c>IMessageHandler&lt;TMessage, TMessageResult&gt;</c>) whose message type is a command.
///     </para>
///     <para>
///         Stream handlers are not covered: streaming answers a sequence of results, which is a query shape, so a
///         stream handler constrained to commands has no pipeline that would call it.
///     </para>
/// </remarks>
internal static class CommandHandlerAnalysis
{
    /// <summary>
    ///     Open generic command handler interfaces.
    /// </summary>
    private static readonly string[] CommandHandlerInterfaceMetadataNames =
    [
        "LiteBus.Commands.Abstractions.ICommandHandler`1",
        "LiteBus.Commands.Abstractions.ICommandHandler`2"
    ];

    /// <summary>
    ///     Open generic messaging-level main handler interfaces.
    /// </summary>
    private static readonly string[] MessagingHandlerInterfaceMetadataNames =
    [
        "LiteBus.Messaging.Abstractions.IAsyncMessageHandler`1",
        "LiteBus.Messaging.Abstractions.IAsyncMessageHandler`2",
        "LiteBus.Messaging.Abstractions.IMessageHandler`2"
    ];

    /// <summary>
    ///     Command message marker interfaces.
    /// </summary>
    private static readonly string[] CommandMarkerMetadataNames =
    [
        "LiteBus.Commands.Abstractions.ICommand",
        "LiteBus.Commands.Abstractions.ICommand`1"
    ];

    /// <summary>
    ///     Query message marker interfaces.
    /// </summary>
    private static readonly string[] QueryMarkerMetadataNames =
    [
        "LiteBus.Queries.Abstractions.IQuery",
        "LiteBus.Queries.Abstractions.IQuery`1",
        "LiteBus.Queries.Abstractions.IStreamQuery`1"
    ];

    /// <summary>
    ///     Determines whether the type is a command handler.
    /// </summary>
    /// <param name="type">The candidate handler type symbol.</param>
    /// <param name="compilation">The compilation being analyzed.</param>
    /// <returns><see langword="true" /> when the type handles commands; otherwise, <see langword="false" />.</returns>
    internal static bool IsCommandHandler(INamedTypeSymbol type, Compilation compilation)
    {
        foreach (var metadataName in CommandHandlerInterfaceMetadataNames)
        {
            if (LiteBusSymbols.ImplementsGenericInterface(type, compilation, metadataName))
            {
                return true;
            }
        }

        return ImplementsMessagingHandlerForCommands(type, compilation);
    }

    /// <summary>
    ///     Determines whether the message type is a command.
    /// </summary>
    /// <param name="messageType">The candidate message type symbol.</param>
    /// <param name="compilation">The compilation being analyzed.</param>
    /// <returns><see langword="true" /> when the message type is a command; otherwise, <see langword="false" />.</returns>
    internal static bool IsCommandMessage(ITypeSymbol messageType, Compilation compilation)
    {
        var markers = GetMarkers(compilation, CommandMarkerMetadataNames);

        return markers.Count > 0 &&
            MatchesAnyMarker(messageType, markers, new HashSet<ITypeParameterSymbol>(SymbolEqualityComparer.Default));
    }

    /// <summary>
    ///     Determines whether the message type is a query or a stream query.
    /// </summary>
    /// <param name="messageType">The candidate message type symbol.</param>
    /// <param name="compilation">The compilation being analyzed.</param>
    /// <returns><see langword="true" /> when the message type is a query; otherwise, <see langword="false" />.</returns>
    /// <remarks>
    ///     LB1023 uses this to recognize <c>IMessageMediator.Mediate</c> calls that only read: mediating a query
    ///     nests no unit of work, so it is not reported.
    /// </remarks>
    internal static bool IsQueryMessage(ITypeSymbol messageType, Compilation compilation)
    {
        var markers = GetMarkers(compilation, QueryMarkerMetadataNames);

        return markers.Count > 0 &&
            MatchesAnyMarker(messageType, markers, new HashSet<ITypeParameterSymbol>(SymbolEqualityComparer.Default));
    }

    /// <summary>
    ///     Determines whether the type implements a messaging-level main handler for commands.
    /// </summary>
    /// <param name="type">The candidate handler type symbol.</param>
    /// <param name="compilation">The compilation being analyzed.</param>
    /// <returns><see langword="true" /> when a messaging handler message type is a command; otherwise, <see langword="false" />.</returns>
    private static bool ImplementsMessagingHandlerForCommands(INamedTypeSymbol type, Compilation compilation)
    {
        List<INamedTypeSymbol> handlers = [];

        foreach (var metadataName in MessagingHandlerInterfaceMetadataNames)
        {
            var handler = compilation.GetTypeByMetadataName(metadataName);

            if (handler is not null)
            {
                handlers.Add(handler);
            }
        }

        if (handlers.Count == 0)
        {
            return false;
        }

        foreach (var candidate in type.AllInterfaces)
        {
            foreach (var handler in handlers)
            {
                if (SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, handler) &&
                    candidate.TypeArguments.Length > 0 &&
                    IsCommandMessage(candidate.TypeArguments[0], compilation))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    ///     Resolves marker interfaces from the compilation.
    /// </summary>
    /// <param name="compilation">The compilation being analyzed.</param>
    /// <param name="markerMetadataNames">The marker interface metadata names.</param>
    /// <returns>The resolved marker interfaces.</returns>
    private static List<INamedTypeSymbol> GetMarkers(Compilation compilation, string[] markerMetadataNames)
    {
        List<INamedTypeSymbol> markers = [];

        foreach (var metadataName in markerMetadataNames)
        {
            var marker = compilation.GetTypeByMetadataName(metadataName);

            if (marker is not null)
            {
                markers.Add(marker);
            }
        }

        return markers;
    }

    /// <summary>
    ///     Determines whether the message type matches one of the marker interfaces.
    /// </summary>
    /// <param name="messageType">The candidate message type symbol.</param>
    /// <param name="markers">The resolved marker interfaces.</param>
    /// <param name="visited">The type parameters already inspected while following constraints.</param>
    /// <returns><see langword="true" /> when the message type matches a marker; otherwise, <see langword="false" />.</returns>
    private static bool MatchesAnyMarker(
        ITypeSymbol messageType,
        List<INamedTypeSymbol> markers,
        HashSet<ITypeParameterSymbol> visited)
    {
        if (messageType is ITypeParameterSymbol typeParameter)
        {
            if (!visited.Add(typeParameter))
            {
                return false;
            }

            foreach (var constraint in typeParameter.ConstraintTypes)
            {
                if (MatchesAnyMarker(constraint, markers, visited))
                {
                    return true;
                }
            }

            return false;
        }

        foreach (var marker in markers)
        {
            if (SymbolEqualityComparer.Default.Equals(messageType.OriginalDefinition, marker))
            {
                return true;
            }
        }

        foreach (var candidate in messageType.AllInterfaces)
        {
            foreach (var marker in markers)
            {
                if (SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, marker))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
