using System;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Runtime.Abstractions.Diagnostics;

/// <summary>
///     Executes manifest diagnostic probes with descriptor name validation.
/// </summary>
public static class DiagnosticCheckExecution
{
    /// <summary>
    ///     Executes a diagnostic probe and validates that <see cref="IDiagnosticCheck.Name" /> matches the manifest descriptor.
    /// </summary>
    /// <param name="descriptor">The manifest descriptor for the probe.</param>
    /// <param name="check">The resolved probe implementation.</param>
    /// <param name="cancellationToken">A token that cancels probe execution.</param>
    /// <returns>The probe result.</returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="descriptor" /> or <paramref name="check" /> is <see langword="null" />.
    /// </exception>
    /// <exception cref="DiagnosticCheckNameMismatchException">
    ///     Thrown when <see cref="IDiagnosticCheck.Name" /> differs from <see cref="DiagnosticCheckDescriptor.Name" />.
    /// </exception>
    public static async Task<DiagnosticResult> CheckAsync(
        DiagnosticCheckDescriptor descriptor,
        IDiagnosticCheck check,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(check);

        if (!string.Equals(check.Name, descriptor.Name, StringComparison.Ordinal))
        {
            throw new DiagnosticCheckNameMismatchException(descriptor.ImplementationType, descriptor.Name, check.Name);
        }

        return await check.CheckAsync(cancellationToken).ConfigureAwait(false);
    }
}
