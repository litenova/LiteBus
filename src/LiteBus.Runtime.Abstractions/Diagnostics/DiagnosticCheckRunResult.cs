using System.Collections.Generic;
using System.Diagnostics;

namespace LiteBus.Runtime.Abstractions.Diagnostics;

/// <summary>
///     The aggregated result of running manifest diagnostic probes.
/// </summary>
/// <param name="Status">The aggregate status derived from probe outcomes.</param>
/// <param name="Probes">The individual probe outcomes, if any.</param>
[DebuggerDisplay("{Status}, Probes={Probes.Count}")]
public sealed record DiagnosticCheckRunResult(
    DiagnosticAggregateStatus Status,
    IReadOnlyList<DiagnosticProbeOutcome> Probes);
