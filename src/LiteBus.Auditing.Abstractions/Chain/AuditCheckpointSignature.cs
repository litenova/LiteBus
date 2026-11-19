using LiteBus.Messaging.Abstractions.Auditing;

namespace LiteBus.Auditing.Abstractions;

/// <summary>
///     A signature over an audit checkpoint.
/// </summary>
/// <param name="Scheme">The signing scheme, such as <c>hmac-sha256</c> or a key identifier.</param>
/// <param name="Value">The encoded signature.</param>
public sealed record AuditCheckpointSignature(string Scheme, string Value);
