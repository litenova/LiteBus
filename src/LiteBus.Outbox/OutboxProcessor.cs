using System;
using LiteBus.Outbox.Abstractions;
using Microsoft.Extensions.Logging;

namespace LiteBus.Outbox;

/// <summary>
///     Obsolete alias for <see cref="LegacySequentialOutboxProcessor" /> retained for backward compatibility.
/// </summary>
[Obsolete("Use LegacySequentialOutboxProcessor or configure OutboxProcessorOptions.Architecture instead.")]
public sealed class OutboxProcessor : LegacySequentialOutboxProcessor
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="OutboxProcessor" /> class.
    /// </summary>
    /// <param name="processingStore">The processing store used to lease and persist envelope state transitions.</param>
    /// <param name="dispatcher">The dispatcher used to publish leased messages.</param>
    /// <param name="options">The batch, lease, owner, and retry settings for this processor instance.</param>
    /// <param name="clock">The time provider used for leasing and retry timestamps.</param>
    /// <param name="logger">The optional logger for lease, pass, and dispatch diagnostics.</param>
    public OutboxProcessor(
        IOutboxProcessingStore processingStore,
        IOutboxDispatcher dispatcher,
        OutboxProcessorOptions options,
        TimeProvider clock,
        ILogger<OutboxProcessor>? logger = null)
        : base(processingStore, dispatcher, options, clock, logger)
    {
    }
}
