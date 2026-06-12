namespace LiteBus.Inbox.Ingress;

/// <summary>
///     Controls how transport deliveries map to inbox acceptance metadata during ingress.
/// </summary>
/// <param name="RequireStableIdentity">
///     A value indicating whether ingress must derive identity and idempotency from the broker delivery id. When
///     <see langword="false" />, missing broker ids fall back to generated identity and no idempotency key.
/// </param>
/// <param name="TrustApplicationHeaders">
///     A value indicating whether LiteBus application headers such as message id, idempotency, and tenant may override
///     broker-scoped defaults. When <see langword="false" />, only the broker delivery id supplies identity and idempotency.
/// </param>
internal sealed record TransportInboxIngressMappingOptions(
    bool RequireStableIdentity = true,
    bool TrustApplicationHeaders = false);
