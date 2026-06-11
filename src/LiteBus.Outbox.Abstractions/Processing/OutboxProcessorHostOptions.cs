using LiteBus.Messaging.Abstractions.Processing;

namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Controls the outbox processor background service lifecycle and polling.
/// </summary>
public sealed class OutboxProcessorHostOptions : ProcessorHostOptions;