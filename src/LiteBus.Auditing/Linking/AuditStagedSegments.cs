using System;
using System.Collections.Generic;

namespace LiteBus.Auditing.Linking;

/// <summary>
///     Collects the segments one mediation staged audit records into, so the commit listener links exactly
///     those after the unit of work commits.
/// </summary>
/// <remarks>
///     Registered as a scoped service when the effective capture stages with the business change for linking
///     after commit. A command batch shares one scope across its commands, so one instance holds every
///     command's staged segments. Access is synchronized because handlers may stage from parallel work
///     inside one mediation.
/// </remarks>
internal sealed class AuditStagedSegments
{
    /// <summary>
    ///     Guards <see cref="_segments" />.
    /// </summary>
    private readonly object _gate = new();

    /// <summary>
    ///     The recorded segments, with repeats collapsed to one entry.
    /// </summary>
    private readonly HashSet<string> _segments = new(StringComparer.Ordinal);

    /// <summary>
    ///     Gets a value indicating whether nothing is recorded.
    /// </summary>
    public bool IsEmpty
    {
        get
        {
            lock (_gate)
            {
                return _segments.Count == 0;
            }
        }
    }

    /// <summary>
    ///     Records one segment a success record was staged into.
    /// </summary>
    /// <param name="segment">The segment the staged record belongs to.</param>
    public void Add(string segment)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(segment);

        lock (_gate)
        {
            _segments.Add(segment);
        }
    }

    /// <summary>
    ///     Returns the recorded segments in ordinal order and clears the collection, so one commit links each
    ///     segment once.
    /// </summary>
    /// <returns>The segments recorded since the last call.</returns>
    public IReadOnlyList<string> Drain()
    {
        lock (_gate)
        {
            List<string> drained = [.. _segments];
            drained.Sort(StringComparer.Ordinal);
            _segments.Clear();
            return drained;
        }
    }
}
