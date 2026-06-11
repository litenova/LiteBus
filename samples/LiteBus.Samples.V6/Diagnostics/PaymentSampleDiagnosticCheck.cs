using LiteBus.Runtime.Abstractions.Diagnostics;

namespace LiteBus.Samples.V6.Diagnostics;

/// <summary>
///     A sample diagnostic probe that reports healthy when the payment demo host is running.
/// </summary>
public sealed class PaymentSampleDiagnosticCheck : IDiagnosticCheck
{
    /// <inheritdoc />
    public string Name => "payments.sample.health";

    /// <inheritdoc />
    public Task<DiagnosticResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new DiagnosticResult(
            DiagnosticStatus.Healthy,
            "Payment sample host is running.",
            new Dictionary<string, object> { ["component"] = "payments" }));
    }
}