using LiteBus.Messaging.Abstractions;

namespace LiteBus.Events.Abstractions;

/// <summary>
///     Represents a handler that executes when any event mediation ends, whatever the outcome.
/// </summary>
/// <remarks>
///     Event completion handlers run on every path: success, short-circuit, denial, failure, and cancellation. Because an
///     event may reach several handlers, the outcome describes the broadcast as a whole.
/// </remarks>
public interface IEventCompletionHandler : IMessageCompletionHandler<IEvent>;
