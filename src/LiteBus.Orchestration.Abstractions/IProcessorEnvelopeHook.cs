namespace LiteBus.Orchestration.Abstractions;

/// <summary>
///     Participates in durable message dispatch by running logic before and after processor dispatch.
/// </summary>
/// <remarks>
///     <para>
///         Host processors invoke <see cref="BeforeDispatchAsync" /> before calling the axis dispatcher and
///         <see cref="AfterDispatchAsync" /> after dispatch succeeds but before terminal persistence while the active
///         lease is still held. Hook failures transition the envelope to dead-letter on the same persist call; the
///         handler is not re-run.
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
    ///     Runs after dispatch completes successfully and before terminal state is persisted while the lease is active.
    /// </summary>
    /// <param name="envelope">The axis-neutral leased envelope view.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>A task that completes after hook post-processing finishes.</returns>
    Task AfterDispatchAsync(IProcessorEnvelope envelope, CancellationToken cancellationToken = default);
}
