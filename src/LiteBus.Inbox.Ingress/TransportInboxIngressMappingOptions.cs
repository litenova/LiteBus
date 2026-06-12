namespace LiteBus.Inbox.Ingress;

/// <summary>
///     Controls how transport deliveries map to inbox acceptance metadata during ingress.
/// </summary>
/// <param name="RequireStableIdentity">
///     A value indicating whether ingress must derive identity and idempotency from the broker delivery id.
/// </param>
/// <param name="TrustApplicationHeaders">
///     A value indicating whether LiteBus application headers such as idempotency and tenant may override broker-scoped
///     defaults.
/// </param>
internal sealed record TransportInboxIngressMappingOptions(
    bool RequireStableIdentity = true,
    bool TrustApplicationHeaders = false);
