using LiteBus.Commands.Abstractions;

namespace LiteBus.Inbox.Abstractions;

/// <summary>
///     Exposes an idempotency key that the scheduler can copy into the durable inbox envelope.
/// </summary>
/// <remarks>
///     <para>
///         Implement this interface when the command already carries a business-level duplicate detection key, such as
///         an HTTP idempotency key or a payment reference. The scheduler uses <see cref="IdempotencyKey" /> only when
///         `CommandScheduleOptions.IdempotencyKey` is empty.
///     </para>
///     <para>
///         Store implementations should treat the key as an acceptance-level duplicate guard. Handlers still need
///         idempotent side effects because inbox processing is at least once.
///     </para>
/// </remarks>
public interface IIdempotentCommand : ICommand
{
    /// <summary>
    ///     Gets the stable key used to identify duplicate command submissions for the same business operation.
    /// </summary>
    string IdempotencyKey { get; }
}