using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Outbox.Abstractions;

namespace LiteBus.Outbox;

/// <summary>
///     Narrows durable outbox writes to integration events.
/// </summary>
/// <remarks>
///     <para>
///         This type is a small facade over <see cref="IOutboxWriter" />. Inject it into application services that should
///         write only externally visible facts. Use <see cref="IOutboxWriter" /> directly only when a module needs to
///         store non-integration event types.
///     </para>
/// </remarks>
public sealed class IntegrationOutbox : IIntegrationOutbox
{
    private readonly IOutboxWriter _outboxWriter;

    /// <summary>
    ///     Initializes a new instance of the <see cref="IntegrationOutbox" /> class.
    /// </summary>
    /// <param name="outboxWriter">The durable outbox writer that performs serialization and storage.</param>
    public IntegrationOutbox(IOutboxWriter outboxWriter)
    {
        _outboxWriter = outboxWriter ?? throw new ArgumentNullException(nameof(outboxWriter));
    }

    /// <inheritdoc />
    public Task<OutboxReceipt<TEvent>> AddAsync<TEvent>(
        TEvent @event,
        OutboxOptions? options = null,
        CancellationToken cancellationToken = default)
        where TEvent : IIntegrationEvent
    {
        return _outboxWriter.AddAsync(@event, options, cancellationToken);
    }
}