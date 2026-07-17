namespace LiteBus.Orchestration.Abstractions;

/// <summary>
///     Participates in durable message dispatch by running logic before and after processor dispatch.
/// </summary>
/// <remarks>
///     <para>
///         Host processors invoke <see cref="BeforeDispatchAsync" /> and <see cref="PrepareDispatchScope" /> before
///         calling the axis dispatcher and
///         <see cref="AfterDispatchAsync" /> after dispatch succeeds but before terminal persistence while the active
///         lease is still held. Before-dispatch failures follow the processor retry policy. After-dispatch failures use
///         the configured hook failure policy without running the handler again.
///     </para>
///     <para>
///         <see cref="PrepareDispatchScope" /> is synchronous so ambient context assignments occur on the same logical
///         execution flow that invokes the dispatcher. The processor invokes <see cref="AbandonDispatchScope" /> when
///         dispatch does not complete or when the after-dispatch hook sequence stops on a failure.
///     </para>
///     <para>
///         Register multiple implementations through dependency injection; hosts resolve
///         <see cref="IEnumerable{T}" /> of <see cref="IProcessorEnvelopeHook" /> and invoke hooks in registration order.
///     </para>
/// </remarks>
public interface IProcessorEnvelopeHook
{
    /// <summary>
    ///     Runs before the axis dispatcher executes one leased envelope.
    /// </summary>
    /// <param name="envelope">The axis-neutral leased envelope view.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>A task that completes before dispatch begins.</returns>
    Task BeforeDispatchAsync(IProcessorEnvelope envelope, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Re-establishes hook-owned ambient state after <see cref="BeforeDispatchAsync" /> completes and before the axis
    ///     dispatcher runs.
    /// </summary>
    /// <remarks>
    ///     This method is synchronous so ambient state is assigned on the logical flow that invokes the dispatcher.
    ///     Perform asynchronous state loading in <see cref="BeforeDispatchAsync" />.
    /// </remarks>
    /// <param name="envelope">The axis-neutral leased envelope view.</param>
    void PrepareDispatchScope(IProcessorEnvelope envelope)
    {
    }

    /// <summary>
    ///     Releases hook-owned dispatch state when dispatch or after-dispatch processing cannot finish.
    /// </summary>
    /// <remarks>
    ///     Implementations must release in-memory state without performing durable writes or throwing an exception.
    /// </remarks>
    /// <param name="envelope">The axis-neutral leased envelope view.</param>
    void AbandonDispatchScope(IProcessorEnvelope envelope)
    {
    }

    /// <summary>
    ///     Runs after dispatch completes successfully and before terminal state is persisted while the lease is active.
    /// </summary>
    /// <param name="envelope">The axis-neutral leased envelope view.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>A task that completes after hook post-processing finishes.</returns>
    Task AfterDispatchAsync(IProcessorEnvelope envelope, CancellationToken cancellationToken = default);
}
