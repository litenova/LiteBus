using System;
using LiteBus.Messaging.Abstractions.DurableMessaging;

namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Maps stored outbox envelopes to enqueue receipts shared by writer implementations.
/// </summary>
internal static class OutboxReceiptMapper
{
    /// <summary>
    ///     Maps a stored envelope to an untyped enqueue receipt.
    /// </summary>
    /// <param name="storedEnvelope">The envelope returned by the store or staging path.</param>
    /// <param name="messageType">The runtime message type used for contract lookup.</param>
    /// <param name="outcome">Whether the store inserted or resolved the envelope.</param>
    /// <returns>The enqueue receipt returned to callers.</returns>
    internal static OutboxReceipt CreateReceipt(
        OutboxEnvelope storedEnvelope,
        Type messageType,
        OutboxEnqueueOutcome outcome)
    {
        return new OutboxReceipt
        {
            Id = storedEnvelope.Id,
            MessageType = messageType,
            Contract = new MessageContractReference
            {
                Name = storedEnvelope.ContractName,
                Version = storedEnvelope.ContractVersion
            },
            StoredAt = storedEnvelope.CreatedAt,
            Trace = ResolveTrace(
                storedEnvelope.CorrelationId,
                storedEnvelope.CausationId,
                storedEnvelope.TraceContext),
            Tenant = ResolveTenant(storedEnvelope.TenantId),
            Outcome = outcome
        };
    }

    /// <summary>
    ///     Maps a stored envelope to a typed enqueue receipt.
    /// </summary>
    /// <typeparam name="TEvent">The compile-time event type associated with the receipt.</typeparam>
    /// <param name="storedEnvelope">The envelope returned by the store or staging path.</param>
    /// <param name="messageType">The runtime message type used for contract lookup.</param>
    /// <param name="outcome">Whether the store inserted or resolved the envelope.</param>
    /// <returns>The typed enqueue receipt returned to callers.</returns>
    internal static OutboxReceipt<TEvent> CreateTypedReceipt<TEvent>(
        OutboxEnvelope storedEnvelope,
        Type messageType,
        OutboxEnqueueOutcome outcome)
        where TEvent : notnull
    {
        var receipt = CreateReceipt(storedEnvelope, messageType, outcome);

        return new OutboxReceipt<TEvent>
        {
            Id = receipt.Id,
            MessageType = receipt.MessageType,
            Contract = receipt.Contract,
            StoredAt = receipt.StoredAt,
            Trace = receipt.Trace,
            Tenant = receipt.Tenant,
            Outcome = receipt.Outcome
        };
    }

    /// <summary>
    ///     Reconstructs trace metadata from persisted envelope columns.
    /// </summary>
    /// <param name="correlationId">The optional correlation identifier stored with the envelope.</param>
    /// <param name="causationId">The optional causation identifier stored with the envelope.</param>
    /// <param name="traceContext">The optional distributed trace context stored with the envelope.</param>
    /// <returns>The trace metadata represented by the stored columns.</returns>
    private static MessageTrace ResolveTrace(
        string? correlationId,
        string? causationId,
        string? traceContext)
    {
        if (!string.IsNullOrWhiteSpace(traceContext) &&
            !string.IsNullOrWhiteSpace(correlationId) &&
            !string.IsNullOrWhiteSpace(causationId))
        {
            return new MessageTrace.Distributed(correlationId, causationId, traceContext);
        }

        if (!string.IsNullOrWhiteSpace(correlationId) && !string.IsNullOrWhiteSpace(causationId))
        {
            return new MessageTrace.Workflow(correlationId, causationId);
        }

        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            return new MessageTrace.Correlated(correlationId);
        }

        return MessageTrace.None.Instance;
    }

    /// <summary>
    ///     Reconstructs tenant metadata from the persisted tenant identifier column.
    /// </summary>
    /// <param name="tenantId">The optional tenant identifier stored with the envelope.</param>
    /// <returns>The tenant metadata represented by the stored column.</returns>
    private static TenantScope ResolveTenant(string? tenantId)
    {
        return string.IsNullOrWhiteSpace(tenantId)
            ? TenantScope.Unscoped.Instance
            : new TenantScope.Isolated(tenantId);
    }
}
