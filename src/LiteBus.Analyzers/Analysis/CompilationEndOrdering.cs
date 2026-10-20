using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace LiteBus.Analyzers.Analysis;

/// <summary>
///     Orders registrations gathered from concurrent symbol actions into source discovery order.
/// </summary>
/// <remarks>
///     Symbol actions run concurrently and in no defined order, while duplicate and missing-handler
///     diagnostics name a first and a second handler. Sorting gathered facts by tree position and span
///     start reproduces the discovery order of a tree walk. The order map is built at compilation end
///     only, where reading the compilation file list binds nothing.
/// </remarks>
internal static class CompilationEndOrdering
{
    /// <summary>
    ///     Gets the position of each syntax tree of the compilation for sorting registrations.
    /// </summary>
    /// <param name="compilation">The compilation being analyzed.</param>
    /// <returns>The zero-based position of each tree in compilation order.</returns>
    internal static Dictionary<SyntaxTree, int> GetSyntaxTreeOrder(Compilation compilation)
    {
        var order = new Dictionary<SyntaxTree, int>();
        var index = 0;

        foreach (var tree in compilation.SyntaxTrees)
        {
            order[tree] = index;
            index++;
        }

        return order;
    }

    /// <summary>
    ///     Gets the sort key of a source location within the compilation tree order.
    /// </summary>
    /// <param name="location">The location to key.</param>
    /// <param name="order">The tree order map from <see cref="GetSyntaxTreeOrder" />.</param>
    /// <returns>The tree position and span start, or <see langword="null" /> when the location has no tree in the map.</returns>
    internal static (int TreeIndex, int SpanStart)? GetOrderKey(Location location, Dictionary<SyntaxTree, int> order)
    {
        if (location.SourceTree is SyntaxTree tree && order.TryGetValue(tree, out var index))
        {
            return (index, location.SourceSpan.Start);
        }

        return null;
    }
}
