using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace LiteBus.Analyzers.Analysis;

/// <summary>
///     Shared helpers for handler coverage of message types declared in referenced assemblies.
/// </summary>
internal static class HandlerCoverageAnalysis
{
    /// <summary>
    ///     Gets the message types of one kind declared in referenced assemblies that name the analyzed compilation
    ///     and lack a main handler.
    /// </summary>
    /// <param name="compilation">The compilation being analyzed.</param>
    /// <param name="typeCache">The per-compilation type cache.</param>
    /// <param name="kind">The message kind to collect.</param>
    /// <param name="mainHandlers">The discovered main handler registrations.</param>
    /// <param name="openGenericHandlers">The discovered open generic main handler type definitions.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The unhandled referenced message types, ordered by assembly and metadata name.</returns>
    internal static ImmutableArray<INamedTypeSymbol> GetUnhandledReferencedMessages(
        Compilation compilation,
        LiteBusTypeCache typeCache,
        MessageKind kind,
        ImmutableArray<HandlerRegistration> mainHandlers,
        ImmutableArray<INamedTypeSymbol> openGenericHandlers,
        CancellationToken cancellationToken)
    {
        var markers = MessageAnalysis.GetMarkerMetadataNames(kind);
        List<INamedTypeSymbol> unhandled = [];

        foreach (var assembly in compilation.SourceModule.ReferencedAssemblySymbols
                     .OrderBy(candidate => candidate.Name, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!ContractRegistrationAnalysis.IsRegisteredInCurrentAssembly(assembly, compilation))
            {
                continue;
            }

            foreach (var type in ContractRegistrationAnalysis.GetDeclaredTypes(assembly))
            {
                if (!MessageAnalysis.IsAnalyzableMessageType(type) ||
                    !MessageAnalysis.ImplementsAnyMarkerInterface(type, typeCache, markers))
                {
                    continue;
                }

                if (kind == MessageKind.Query && MessageAnalysis.IsStreamQueryType(type, typeCache))
                {
                    continue;
                }

                if (MessageAnalysis.HasMainHandler(type, mainHandlers, openGenericHandlers, compilation))
                {
                    continue;
                }

                unhandled.Add(type);
            }
        }

        return unhandled
            .OrderBy(type => type.ContainingAssembly.Name, StringComparer.Ordinal)
            .ThenBy(type => LiteBusSymbols.GetMetadataName(type), StringComparer.Ordinal)
            .ToImmutableArray();
    }

    /// <summary>
    ///     Gets the first location of the analyzed compilation, where cross-assembly handler coverage is reported.
    /// </summary>
    /// <param name="compilation">The compilation being analyzed.</param>
    /// <returns>
    ///     The location of the first token of the compilation's first syntax tree, or
    ///     <see cref="Location.None" /> when the compilation has no syntax trees.
    /// </returns>
    /// <remarks>
    ///     A message type declared in a referenced assembly has no source location in the analyzed compilation, so a
    ///     missing handler for it is reported at the earliest location the author can open.
    /// </remarks>
    internal static Location GetFirstCompilationLocation(Compilation compilation)
    {
        var tree = compilation.SyntaxTrees.FirstOrDefault();

        if (tree is null)
        {
            return Location.None;
        }

        return tree.GetRoot().GetFirstToken().GetLocation();
    }
}
