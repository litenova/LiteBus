namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Describes the outcome of one inbox or outbox processor pass.
/// </summary>
/// <remarks>
///     <para>
///         A pass leases a bounded batch and processes each leased envelope before returning. The leased count helps
///         hosted processors apply adaptive polling when a full batch indicates more work may be waiting.
///     </para>
/// </remarks>
public sealed record ProcessorPassResult
{
    /// <summary>
    ///     Gets the number of commands or messages leased and processed during the pass.
    /// </summary>
    public required int LeasedCount { get; init; }
}
