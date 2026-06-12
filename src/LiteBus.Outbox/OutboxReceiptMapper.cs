using System;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions.DurableMessaging;
using LiteBus.Outbox.Abstractions;

namespace LiteBus.Outbox;

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
    /// <returns>The enqueue receipt returned to callers.</returns>
    internal static OutboxReceipt CreateReceipt(OutboxEnvelope storedEnvelope, Type messageType)
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
            Trace = DurableEnvelopeMetadataMapper.ResolveTrace(
                storedEnvelope.CorrelationId,
                storedEnvelope.CausationId,
                storedEnvelope.TraceContext),
            Tenant = DurableEnvelopeMetadataMapper.ResolveTenant(storedEnvelope.TenantId)
        };
    }

    /// <summary>
    ///     Maps a stored envelope to a typed enqueue receipt.
    /// </summary>
    /// <typeparam name="TEvent">The compile-time event type associated with the receipt.</typeparam>
    /// <param name="storedEnvelope">The envelope returned by the store or staging path.</param>
    /// <param name="messageType">The runtime message type used for contract lookup.</param>
    /// <returns>The typed enqueue receipt returned to callers.</returns>
    internal static OutboxReceipt<TEvent> CreateTypedReceipt<TEvent>(OutboxEnvelope storedEnvelope, Type messageType)
        where TEvent : notnull
    {
        var receipt = CreateReceipt(storedEnvelope, messageType);

        return new OutboxReceipt<TEvent>
        {
            Id = receipt.Id,
            MessageType = receipt.MessageType,
            Contract = receipt.Contract,
            StoredAt = receipt.StoredAt,
            Trace = receipt.Trace,
            Tenant = receipt.Tenant
        };
    }
}
