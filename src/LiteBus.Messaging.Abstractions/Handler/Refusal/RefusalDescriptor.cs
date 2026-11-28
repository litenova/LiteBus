namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Describes what one refusal code means, independent of any transport.
/// </summary>
/// <param name="Kind">The refusal kind used for default mappings.</param>
/// <param name="Title">The stable short title.</param>
/// <param name="Retryable">Whether retrying the same request can succeed.</param>
/// <remarks>
///     LiteBus ships descriptors for its own codes, and products contribute entries for theirs. Transports resolve a
///     code to its descriptor and fall back to the kind default when no entry matches, so every transport renders an
///     unmapped code the same way.
/// </remarks>
public sealed record RefusalDescriptor(RefusalKind Kind, string Title, bool Retryable);
