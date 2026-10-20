using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace LiteBus.Analyzers.Analysis;

/// <summary>
///     Caches compilation type resolutions so handler and message collection passes resolve each metadata name once.
/// </summary>
/// <remarks>
///     Each analyzer constructs its own cache per compilation action and never shares it across actions.
/// </remarks>
internal sealed class LiteBusTypeCache
{
    /// <summary>
    ///     Guards the type cache against concurrent syntax node actions within one compilation.
    /// </summary>
    private readonly object _lock = new();

    /// <summary>
    ///     Resolved types keyed by metadata name, including names that resolved to no symbol.
    /// </summary>
    private readonly Dictionary<string, INamedTypeSymbol?> _types =
        new(StringComparer.Ordinal);

    /// <summary>
    ///     Initializes a new instance of the <see cref="LiteBusTypeCache" /> class.
    /// </summary>
    /// <param name="compilation">The compilation being analyzed.</param>
    internal LiteBusTypeCache(Compilation compilation)
    {
        Compilation = compilation;
    }

    /// <summary>
    ///     Gets the compilation being analyzed.
    /// </summary>
    internal Compilation Compilation { get; }

    /// <summary>
    ///     Gets the number of distinct metadata names resolved through this cache.
    /// </summary>
    internal int LookupCount { get; private set; }

    /// <summary>
    ///     Resolves a type symbol from the compilation using its metadata name, caching the result.
    /// </summary>
    /// <param name="metadataName">The fully qualified metadata name.</param>
    /// <returns>The resolved type symbol, if present.</returns>
    internal INamedTypeSymbol? GetType(string metadataName)
    {
        lock (_lock)
        {
            if (_types.TryGetValue(metadataName, out var cached))
            {
                return cached;
            }

            LookupCount++;
            var resolved = Compilation.GetTypeByMetadataName(metadataName);
            _types[metadataName] = resolved;
            return resolved;
        }
    }
}
