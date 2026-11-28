using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Read-only surface for refusal catalog resolution.
///     Consumed by transports at runtime.
/// </summary>
/// <remarks>
///     <para>
///         Resolution order is the exact code mapping, then the longest prefix mapping, then nothing: the catalog
///         reports whether a mapping matches and leaves defaulting to the transport, so there is one defaulting
///         story per transport instead of a neutral default competing with it.
///     </para>
/// </remarks>
public interface IRefusalReader
{
    /// <summary>
    ///     Gets every registered mapping with its owner, ordered by pattern.
    /// </summary>
    IReadOnlyList<RefusalEntry> Entries { get; }

    /// <summary>
    ///     Tries to resolve the mapping for a refusal code, reporting whether a mapping matches.
    /// </summary>
    /// <param name="code">The refusal code, or <see langword="null" /> when the decision supplied none.</param>
    /// <param name="entry">The matching mapping, or <see langword="null" /> when nothing matches.</param>
    /// <returns><see langword="true" /> when an exact or prefix mapping matches; otherwise, <see langword="false" />.</returns>
    bool TryResolve(string? code, [NotNullWhen(true)] out RefusalEntry? entry);
}
