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
    internal static readonly ImmutableArray<string> ImpureDependencyMetadataNames = ImmutableArray.Create(
        "LiteBus.Commands.Abstractions.ICommandMediator",
        "LiteBus.Events.Abstractions.IEventMediator",
        "LiteBus.Events.Abstractions.IEventPublisher",
        "LiteBus.Inbox.Abstractions.IInbox",
        "LiteBus.Outbox.Abstractions.IOutbox");

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
}
