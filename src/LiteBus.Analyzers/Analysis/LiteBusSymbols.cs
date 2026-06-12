using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace LiteBus.Analyzers.Analysis;

/// <summary>
///     Well-known LiteBus metadata names and helper methods used by analyzers.
/// </summary>
internal static class LiteBusSymbols
{
    /// <summary>
    ///     Side-effecting dependency types that query handlers must not use.
    /// </summary>
    internal static readonly ImmutableArray<string> ImpureDependencyMetadataNames =
    [
        "LiteBus.Commands.Abstractions.ICommandMediator",
        "LiteBus.Events.Abstractions.IEventMediator",
        "LiteBus.Queries.Abstractions.IQueryMediator",
        "LiteBus.Inbox.Abstractions.IInbox",
        "LiteBus.Inbox.Abstractions.ITransactionalInbox`1",
        "LiteBus.Inbox.Abstractions.IInboxStore",
        "LiteBus.Inbox.Abstractions.ITransactionalInboxStore",
        "LiteBus.Outbox.Abstractions.IOutbox",
        "LiteBus.Outbox.Abstractions.IOutboxStore",
        "LiteBus.Outbox.Abstractions.ITransactionalOutboxStore",
        "LiteBus.Transport.Abstractions.IMessageTransport"
    ];

    /// <summary>
    ///     Resolves a type symbol from the compilation using its metadata name.
    /// </summary>
    /// <param name="compilation">The compilation being analyzed.</param>
    /// <param name="metadataName">The fully qualified metadata name.</param>
    /// <returns>The resolved type symbol, if present.</returns>
    internal static INamedTypeSymbol? GetType(Compilation compilation, string metadataName)
    {
        return compilation.GetTypeByMetadataName(metadataName);
    }

    /// <summary>
    ///     Determines whether <paramref name="type" /> implements or inherits from <paramref name="baseType" />.
    /// </summary>
    /// <param name="type">The candidate type symbol.</param>
    /// <param name="baseType">The base interface or class symbol.</param>
    /// <returns><see langword="true" /> when the type implements the base type; otherwise, <see langword="false" />.</returns>
    internal static bool Implements(INamedTypeSymbol type, INamedTypeSymbol? baseType)
    {
        if (baseType is null)
        {
            return false;
        }

        foreach (var candidate in type.AllInterfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, baseType))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Determines whether <paramref name="type" /> implements a generic interface with the given metadata name.
    /// </summary>
    /// <param name="type">The candidate type symbol.</param>
    /// <param name="compilation">The compilation being analyzed.</param>
    /// <param name="interfaceMetadataName">The open generic interface metadata name.</param>
    /// <returns><see langword="true" /> when the type implements the interface; otherwise, <see langword="false" />.</returns>
    internal static bool ImplementsGenericInterface(
        INamedTypeSymbol type,
        Compilation compilation,
        string interfaceMetadataName)
    {
        var expected = compilation.GetTypeByMetadataName(interfaceMetadataName);

        if (expected is null)
        {
            return false;
        }

        foreach (var candidate in type.AllInterfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, expected))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Gets implemented handler interfaces that match one of the supplied open generic metadata names.
    /// </summary>
    /// <param name="type">The handler type symbol.</param>
    /// <param name="interfaceMetadataNames">Open generic interface metadata names.</param>
    /// <returns>The matching implemented interfaces.</returns>
    internal static ImmutableArray<INamedTypeSymbol> GetImplementedHandlerInterfaces(
        INamedTypeSymbol type,
        params string[] interfaceMetadataNames)
    {
        var builder = ImmutableArray.CreateBuilder<INamedTypeSymbol>();

        foreach (var candidate in type.AllInterfaces)
        {
            var metadataName = candidate.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            foreach (var expected in interfaceMetadataNames)
            {
                if (metadataName == expected)
                {
                    builder.Add(candidate);
                }
            }
        }

        return builder.ToImmutable();
    }

    /// <summary>
    ///     Builds the CLR metadata name for a named type symbol.
    /// </summary>
    /// <param name="symbol">The named type symbol.</param>
    /// <returns>The metadata name used by <see cref="Compilation.GetTypeByMetadataName(string)" />.</returns>
    internal static string GetMetadataName(INamedTypeSymbol symbol)
    {
        if (symbol.ContainingType is not null)
        {
            return GetMetadataName(symbol.ContainingType) + "+" + symbol.MetadataName;
        }

        if (symbol.ContainingNamespace is { IsGlobalNamespace: false } containingNamespace)
        {
            return containingNamespace.ToDisplayString() + "." + symbol.MetadataName;
        }

        return symbol.MetadataName;
    }

    /// <summary>
    ///     Retargets a named type symbol to the compilation's unified symbol model.
    /// </summary>
    /// <param name="compilation">The compilation being analyzed.</param>
    /// <param name="symbol">The candidate type symbol.</param>
    /// <returns>The retargeted type symbol when found; otherwise, the original symbol.</returns>
    internal static INamedTypeSymbol RetargetToCompilation(Compilation compilation, INamedTypeSymbol symbol)
    {
        var metadataName = GetMetadataName(symbol);
        var retargeted = compilation.GetTypeByMetadataName(metadataName);

        return retargeted ?? symbol;
    }

    /// <summary>
    ///     Gets a diagnostic location that belongs to the compilation being analyzed.
    /// </summary>
    /// <param name="compilation">The compilation being analyzed.</param>
    /// <param name="location">The candidate location.</param>
    /// <returns>The original location when it is in the compilation; otherwise, <see cref="Location.None" />.</returns>
    internal static Location GetDiagnosticLocation(Compilation compilation, Location location)
    {
        if (location.SourceTree is not null && compilation.ContainsSyntaxTree(location.SourceTree))
        {
            return location;
        }

        return Location.None;
    }
}