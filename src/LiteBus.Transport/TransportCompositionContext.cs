namespace LiteBus.Transport;

/// <summary>
///     Records which transport module claimed the single transport slot of a module configuration.
/// </summary>
internal sealed class TransportCompositionContext
{
    /// <summary>
    ///     Gets or sets the first transport module type that registered transport metrics for this composition.
    /// </summary>
    /// <value>
    ///     The claiming module type, or <see langword="null" /> when no transport module has registered yet.
    /// </value>
    public Type? ClaimedBy { get; set; }
}
