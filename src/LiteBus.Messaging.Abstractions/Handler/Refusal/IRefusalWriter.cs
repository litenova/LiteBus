namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Write-only surface for refusal catalog registration.
///     Consumed by module builders and declaration sources at configuration time.
/// </summary>
/// <remarks>
///     <para>
///         An application contributes its own codes with <see cref="Map" />, and a declaration source calls the same
///         methods, so every transport reads product refusals from one registry instead of each keeping its own map.
///         A code ending in <c>.*</c> is a prefix pattern that covers every code under that prefix, such as
///         <c>authorization.*</c> covering <c>authorization.forbidden</c>.
///     </para>
///     <para>
///         Registrations follow one conflict rule: mapping a pattern that already carries the same kind, title, and
///         retryability is a no-op, mapping it to a different meaning throws, and a deliberate override calls
///         <see cref="Replace" />.
///     </para>
///     <para>
///         Fault codes are never registered here. A fault is carried by <see cref="MessageFaultException" />, ends the
///         mediation as <see cref="MediationOutcome.Failed" />, and is answered as a server error without consulting
///         the catalog, so refusal rewriting can never turn it into a caller-actionable end.
///     </para>
/// </remarks>
public interface IRefusalWriter
{
    /// <summary>
    ///     Maps a refusal code to its transport-neutral meaning.
    /// </summary>
    /// <param name="code">The exact refusal code, or a prefix pattern ending in <c>.*</c>.</param>
    /// <param name="kind">The refusal kind transports map to their own status.</param>
    /// <param name="title">The stable short title.</param>
    /// <param name="retryable">Whether retrying the same request can succeed.</param>
    /// <param name="owner">The module or component registering the mapping, recorded for the composition model.</param>
    /// <returns>The writer for chaining.</returns>
    IRefusalWriter Map(string code, RefusalKind kind, string title, bool retryable, string? owner = null);

    /// <summary>
    ///     Maps a refusal code to its transport-neutral meaning, replacing any mapping the pattern carries.
    /// </summary>
    /// <param name="code">The exact refusal code, or a prefix pattern ending in <c>.*</c>.</param>
    /// <param name="kind">The refusal kind transports map to their own status.</param>
    /// <param name="title">The stable short title.</param>
    /// <param name="retryable">Whether retrying the same request can succeed.</param>
    /// <param name="owner">The module or component registering the mapping, recorded for the composition model.</param>
    /// <returns>The writer for chaining.</returns>
    IRefusalWriter Replace(string code, RefusalKind kind, string title, bool retryable, string? owner = null);
}
