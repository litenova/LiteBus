using LiteBus.Messaging.Abstractions.Processing;

namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Allows the outbox processor loop to wait for new work or fall back to a polling delay.
/// </summary>
public interface IOutboxWorkSignal : IProcessorWorkSignal;
