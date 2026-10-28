using LiteBus.Messaging.Abstractions.Processing;

namespace LiteBus.Inbox.Abstractions;

/// <summary>
///     Allows the inbox processor loop to wait for new work or fall back to a polling delay.
/// </summary>
public interface IInboxWorkSignal : IProcessorWorkSignal;
