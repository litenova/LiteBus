using LiteBus.Messaging.Abstractions.Processing;

namespace LiteBus.Inbox.Abstractions;

/// <summary>
///     Leases due inbox envelopes and dispatches them through <see cref="IInboxDispatcher" />.
/// </summary>
/// <remarks>
///     <para>
///         Host this processor from a worker, timer, hosted service, or manual maintenance job. Each call performs one
///         processing pass: lease a batch, dispatch each envelope, and record completion, retry, or dead-letter state.
///     </para>
///     <para>
///         Processing is at least once. Dispatch targets and handlers should be idempotent around external side effects
///         and database writes that can be retried.
///     </para>
/// </remarks>
public interface IInboxProcessor : IMessageProcessor
{
}