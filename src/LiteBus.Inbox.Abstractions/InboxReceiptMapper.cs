using System;
using LiteBus.Messaging.Abstractions.DurableMessaging;

namespace LiteBus.Inbox.Abstractions;

/// <summary>
///     Maps stored inbox envelopes to acceptance receipts shared by writer implementations.
/// </summary>
internal static class InboxReceiptMapper
{
    /// <summary>
    ///     Maps a stored envelope to a typed acceptance receipt.
    /// </summary>
    /// <typeparam name="TMessage">The compile-time message type associated with the acceptance command.</typeparam>
    /// <param name="storedEnvelope">The envelope returned by the store or staging path.</param>
    /// <param name="messageType">The runtime message type used for contract lookup.</param>
    /// <param name="outcome">Whether the store accepted a new row or returned an existing one.</param>
    /// <returns>The typed acceptance receipt returned to callers.</returns>
    internal static InboxReceipt<TMessage> CreateTypedReceipt<TMessage>(
        InboxEnvelope storedEnvelope,
        Type messageType,
        InboxAcceptOutcome outcome)
        where TMessage : notnull
    {
        return new InboxReceipt<TMessage>
        {
            Id = storedEnvelope.Id,
            MessageType = messageType,
            Contract = new MessageContractReference
            {
                Name = storedEnvelope.ContractName,
                Version = storedEnvelope.ContractVersion
            },
            AcceptedAt = storedEnvelope.CreatedAt,
            Trace = ResolveTrace(
                storedEnvelope.CorrelationId,
                storedEnvelope.CausationId,
                storedEnvelope.TraceContext),
            Tenant = ResolveTenant(storedEnvelope.TenantId),
            Outcome = outcome
        };
    }

    /// <summary>
    ///     Maps a stored envelope to an untyped acceptance receipt for batch APIs.
    /// </summary>
    /// <param name="storedEnvelope">The envelope returned by the store or staging path.</param>
    /// <param name="messageType">The runtime message type used for contract lookup.</param>
    /// <param name="outcome">Whether the store accepted a new row or returned an existing one.</param>
    /// <returns>The acceptance receipt returned to batch callers.</returns>
    internal static InboxReceipt CreateUntypedReceipt(
        InboxEnvelope storedEnvelope,
        Type messageType,
        InboxAcceptOutcome outcome)
    {
        return new InboxReceipt
        {
            Id = storedEnvelope.Id,
            MessageType = messageType,
            Contract = new MessageContractReference
            {
                Name = storedEnvelope.ContractName,
                Version = storedEnvelope.ContractVersion
            },
            AcceptedAt = storedEnvelope.CreatedAt,
            Trace = ResolveTrace(
                storedEnvelope.CorrelationId,
                storedEnvelope.CausationId,
                storedEnvelope.TraceContext),
            Tenant = ResolveTenant(storedEnvelope.TenantId),
            Outcome = outcome
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
