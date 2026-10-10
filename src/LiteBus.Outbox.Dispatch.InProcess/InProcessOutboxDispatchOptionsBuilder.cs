namespace LiteBus.Outbox.Dispatch.InProcess;

/// <summary>
///     Collects in-process outbox dispatch settings inside the <c>UseInProcessDispatch</c> configuration action.
/// </summary>
/// <remarks>
///     A mutable builder is used here rather than the immutable <see cref="InProcessOutboxDispatchOptions" /> record
///     so the registration reads as a sequence of assignments, which is the shape every other <c>Use*</c> extension in
///     the outbox module uses.
/// </remarks>
public sealed class InProcessOutboxDispatchOptionsBuilder
{
    /// <summary>
    ///     Gets or sets a value indicating whether a dispatched message with no registered handler fails the dispatch.
    /// </summary>
    /// <value>
    ///     <see langword="true" /> by default. See <see cref="InProcessOutboxDispatchOptions.RequireHandler" /> for
    ///     why a durable dispatch treats an unhandled message as a failure when an immediate publish does not.
    /// </value>
    public bool RequireHandler { get; set; } = true;

    /// <summary>
    ///     Creates the immutable options the module registers.
    /// </summary>
    /// <returns>The configured options.</returns>
    internal InProcessOutboxDispatchOptions Build()
    {
        return new InProcessOutboxDispatchOptions
        {
            RequireHandler = RequireHandler
        };
    }
}
