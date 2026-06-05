using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Runtime.Abstractions.Diagnostics;

/// <summary>
///     Framework-neutral diagnostic probe that applications can map to health endpoints or custom sinks.
/// </summary>
public interface IDiagnosticCheck
{
    /// <summary>
    ///     Gets the stable probe name reported to operators and health hosts.
    /// </summary>
    /// <value>The probe identifier, such as <c>litebus.inbox.queue</c>.</value>
    string Name { get; }

    /// <summary>
    ///     Executes the probe and returns the current diagnostic outcome.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the probe.</param>
    /// <returns>The probe result describing status, summary text, and optional data.</returns>
    Task<DiagnosticResult> CheckAsync(CancellationToken cancellationToken = default);
}
