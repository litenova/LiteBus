using System.Collections.Generic;

namespace LiteBus.Runtime.Abstractions.Diagnostics;

/// <summary>
///     The outcome returned by an <see cref="IDiagnosticCheck" /> execution.
/// </summary>
/// <param name="Status">The reported health status.</param>
/// <param name="Description">A human-readable summary of the probe outcome.</param>
/// <param name="Data">Optional structured values included in health reports.</param>
public sealed record DiagnosticResult(
    DiagnosticStatus Status,
    string Description,
    IReadOnlyDictionary<string, object>? Data = null);
