using System;
using System.Collections.Concurrent;

namespace LiteBus.Analyzers.Analysis;

/// <summary>
///     Collects tag strings referenced by mediation filters during compilation analysis.
/// </summary>
internal sealed class TagReferenceCollector
{
    /// <summary>
    ///     The referenced tags keyed by tag value.
    /// </summary>
    private readonly ConcurrentDictionary<string, byte> _referencedTags = new(StringComparer.Ordinal);

    /// <summary>
    ///     Initializes a new instance of the <see cref="TagReferenceCollector" /> class.
    /// </summary>
    internal TagReferenceCollector()
    {
    }

    /// <summary>
    ///     Adds a referenced tag to the collection.
    /// </summary>
    /// <param name="tag">The referenced tag value.</param>
    internal void Add(string tag)
    {
        _referencedTags.TryAdd(tag, 0);
    }

    /// <summary>
    ///     Determines whether the tag was referenced by a mediation filter.
    /// </summary>
    /// <param name="tag">The tag value to look up.</param>
    /// <returns><see langword="true" /> when the tag was referenced; otherwise, <see langword="false" />.</returns>
    internal bool Contains(string tag)
    {
        return _referencedTags.ContainsKey(tag);
    }
}
