namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Well-known keys LiteBus writes into <see cref="System.Exception.Data" /> during mediation.
/// </summary>
public static class MediationExceptionData
{
    /// <summary>
    ///     The key under which faults raised by completion handlers are attached to the exception that ended the
    ///     mediation.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A completion handler observes an ending and must never replace it, so a fault it raises while the pipeline
    ///         is already failing cannot propagate. Discarding it would lose the one thing an audit trail cannot afford
    ///         to lose: the reason the record was not written. It is attached to the original exception instead, so it
    ///         travels to whoever handles the failure.
    ///     </para>
    ///     <para>
    ///         The value is an <see cref="System.Collections.Generic.IReadOnlyList{T}" /> of
    ///         <see cref="System.Exception" />, in the order the handlers ran.
    ///     </para>
    /// </remarks>
    public const string SuppressedCompletionFaults = "LiteBus.SuppressedCompletionFaults";
}
