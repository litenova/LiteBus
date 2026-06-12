using System.Collections.Generic;
using System.Diagnostics;

namespace LiteBus.Runtime.Abstractions.Diagnostics;

/// <summary>
///     One diagnostic probe outcome collected during a shared diagnostic run.
/// </summary>
/// <param name="Name">The manifest probe name.</param>
/// <param name="Status">The reported probe status.</param>
/// <param name="Description">The probe summary text.</param>
/// <param name="Data">Optional structured values from the probe.</param>
[DebuggerDisplay("{Name} ({Status})")]
public sealed record DiagnosticProbeOutcome(
    string Name,
    DiagnosticStatus Status,
    string Description,
    IReadOnlyDictionary<string, object>? Data);
