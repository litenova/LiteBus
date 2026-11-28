namespace LiteBus.Outbox.Dispatch.InProcess;

/// <summary>
///     Configures how the in-process outbox dispatcher publishes stored events into the local event pipeline.
/// </summary>
public sealed record InProcessOutboxDispatchOptions
{
    /// <summary>
    ///     Gets a value indicating whether a dispatched message with no registered handler fails the dispatch.
    /// </summary>
    /// <value>
    ///     <see langword="true" /> (the default) to fail the dispatch attempt, which retries the message and
    ///     eventually dead-letters it. <see langword="false" /> to complete the dispatch and mark the message
    ///     published without any handler having run.
    /// </value>
    /// <remarks>
    ///     <para>
    ///         An immediate <c>PublishAsync</c> defaults to a silent no-op because the caller is standing right
    ///         there and can see there are no subscribers. A durable dispatch has no such caller: the whole promise
    ///         of the outbox is that a committed message reached somewhere, so a message that reached nothing is a
    ///         failure and not a success.
    ///     </para>
    ///     <para>
    ///         The default matters because the failure it prevents is silent and total. Registering the event axis
    ///         over an assembly that does not hold the handlers leaves every outbox message leased, dispatched and
    ///         marked published without leaving the process, with no exception, no dead letter, and a backlog
    ///         reading of zero. Call <see cref="InProcessOutboxDispatchOptionsBuilder.AllowEnvelopesWithoutHandler" />
    ///         only when publishing to zero handlers is a state the application deliberately allows.
    ///     </para>
    /// </remarks>
    public bool RequireHandler { get; init; } = true;

    /// <summary>
    ///     Gets the acknowledgement recorded when envelopes without a handler are allowed.
    /// </summary>
    /// <value>
    ///     The reason passed to <see cref="InProcessOutboxDispatchOptionsBuilder.AllowEnvelopesWithoutHandler" />,
    ///     or <see langword="null" /> when <see cref="RequireHandler" /> is <see langword="true" />.
    /// </value>
    /// <remarks>
    ///     A startup task logs this reason at warning level so the choice stays visible in production logs.
    /// </remarks>
    public string? MissingHandlerReason { get; init; }
}
