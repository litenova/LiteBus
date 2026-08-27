using System;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Tracks how a mediation is ending and reports it to the completion stage.
/// </summary>
/// <remarks>
///     <para>
///         Every mediation strategy has to record the same four things as it runs, then hand them to the completion
///         stage in a <c>finally</c>. Each strategy used to carry its own copy of that bookkeeping, and copies drift:
///         one of them recorded a refusal differently from the rest.
///     </para>
///     <para>
///         Create one at the top of a strategy, call the matching <c>Record</c> method as each path is taken, and call
///         <see cref="CompleteAsync" /> in a <c>finally</c>. A custom mediation strategy should do the same, which is
///         also what stops it having to know that the completion stage wants an execution-context override in
///         preference to the handler's own result.
///     </para>
///     <para>
///         This is a mutable struct on purpose: one lives per mediation on the stack of an async method, and it must not
///         add an allocation to a path that already has a stopwatch timestamp and four fields. Pass it by reference.
///     </para>
/// </remarks>
public struct MediationOutcome
{
    /// <summary>
    ///     The timestamp the mediation started, used to report its duration.
    /// </summary>
    private readonly long _startedAt;

    /// <summary>
    ///     Initializes a new instance of the <see cref="MediationOutcome" /> struct.
    /// </summary>
    /// <param name="startedAt">The timestamp the mediation started.</param>
    private MediationOutcome(long startedAt)
    {
        _startedAt = startedAt;
        Outcome = MessageOutcome.Succeeded;
    }

    /// <summary>
    ///     Begins tracking a mediation, starting its timer.
    /// </summary>
    /// <returns>A tracker recording a mediation that has not ended yet.</returns>
    public static MediationOutcome Start()
    {
        return new MediationOutcome(Stopwatch.GetTimestamp());
    }

    /// <summary>
    ///     Gets how the mediation ended.
    /// </summary>
    /// <value>
    ///     <see cref="MessageOutcome.Succeeded" /> until something records otherwise, because a mediation that reaches
    ///     the end of its own body without a decision or a fault has succeeded.
    /// </value>
    public MessageOutcome Outcome { get; private set; }

    /// <summary>
    ///     Gets the exception that ended the mediation, when one did.
    /// </summary>
    /// <value>The fault, the cancellation, or the refusal that reached the caller; otherwise <see langword="null" />.</value>
    public Exception? Failure { get; private set; }

    /// <summary>
    ///     Gets the reason a pre-stage decision gave for stopping the pipeline.
    /// </summary>
    /// <value>The reason, or <see langword="null" /> when no decision stopped the mediation.</value>
    public string? Reason { get; private set; }

    /// <summary>
    ///     Records that a pre-stage decision stopped the pipeline.
    /// </summary>
    /// <param name="stop">The decision a guard, validator, or shortcut returned.</param>
    public void RecordStop(PipelineStop stop)
    {
        Outcome = stop.Outcome;
        Reason = stop.Reason;
    }

    /// <summary>
    ///     Records the exception a refusal reaches the caller as.
    /// </summary>
    /// <param name="refusal">The denial or invalid-message exception.</param>
    /// <remarks>
    ///     The outcome is already <see cref="MessageOutcome.Denied" /> or <see cref="MessageOutcome.Invalid" /> from
    ///     <see cref="RecordStop" />, and stays that way: a refusal reaching the caller as an exception is still a
    ///     decision rather than a fault, so it must not become <see cref="MessageOutcome.Failed" />.
    /// </remarks>
    public void RecordRefusal(Exception refusal)
    {
        Failure = refusal;
    }

    /// <summary>
    ///     Records that the mediation was cancelled through the caller's token.
    /// </summary>
    /// <param name="cancellation">The cancellation that ended the mediation.</param>
    public void RecordCancellation(OperationCanceledException cancellation)
    {
        Outcome = MessageOutcome.Canceled;
        Failure = cancellation;
    }

    /// <summary>
    ///     Records that the mediation ended with a fault.
    /// </summary>
    /// <param name="failure">The exception raised somewhere in the pipeline.</param>
    public void RecordFailure(Exception failure)
    {
        Outcome = MessageOutcome.Failed;
        Failure = failure;
    }

    /// <summary>
    ///     Records a fault and offers it to the error stage.
    /// </summary>
    /// <param name="messageDependencies">The message dependencies encapsulating error handlers.</param>
    /// <param name="message">The message being mediated.</param>
    /// <param name="executionContext">The execution context the mediation is running under.</param>
    /// <param name="observedResult">Whatever result existed when the fault happened, for handlers to inspect.</param>
    /// <param name="fault">The exception that ended the mediation.</param>
    /// <returns>
    ///     The error context the handlers saw, carrying whether one of them recovered and what it recovered with.
    /// </returns>
    /// <remarks>
    ///     Recording the fault and running the handlers always go together, and every strategy paired them by hand.
    ///     Keeping them together here is also what stops a strategy running error handlers without first recording that
    ///     the mediation failed, which would report a success to the completion stage for a mediation that threw.
    /// </remarks>
    public Task<MessageErrorContext> RecordFaultAsync(
        IMessageDependencies messageDependencies,
        object message,
        IExecutionContext executionContext,
        object? observedResult,
        Exception fault)
    {
        ArgumentNullException.ThrowIfNull(messageDependencies);
        ArgumentNullException.ThrowIfNull(executionContext);

        RecordFailure(fault);

        return RunErrorHandlersAsync(messageDependencies, message, executionContext, observedResult, fault);
    }

    /// <summary>
    ///     Runs the error handlers for a recorded fault inside the ambient execution scope.
    /// </summary>
    /// <param name="messageDependencies">The message dependencies encapsulating error handlers.</param>
    /// <param name="message">The message being mediated.</param>
    /// <param name="executionContext">The execution context the mediation is running under.</param>
    /// <param name="observedResult">Whatever result existed when the fault happened.</param>
    /// <param name="fault">The exception that ended the mediation.</param>
    /// <returns>The error context the handlers saw.</returns>
    private static async Task<MessageErrorContext> RunErrorHandlersAsync(
        IMessageDependencies messageDependencies,
        object message,
        IExecutionContext executionContext,
        object? observedResult,
        Exception fault)
    {
        using (AmbientExecutionContext.CreateScope(executionContext))
        {
            return await messageDependencies.RunAsyncErrorHandlers(
                message,
                observedResult,
                ExceptionDispatchInfo.Capture(fault),
                executionContext.CancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Reports the ending to every registered completion handler.
    /// </summary>
    /// <param name="messageDependencies">The message dependencies encapsulating completion handlers.</param>
    /// <param name="message">The message that was mediated.</param>
    /// <param name="executionContext">The execution context the mediation ran under.</param>
    /// <param name="messageResult">The result the main handler produced, when it ran and produced one.</param>
    /// <returns>A task that completes once every completion handler has run.</returns>
    /// <remarks>
    ///     A post-handler may have replaced what the caller receives through
    ///     <see cref="IExecutionContext.MessageResult" />, and the completion stage should see what the caller actually
    ///     got. Resolving that here rather than at each call site is the point: getting it wrong reports the handler's
    ///     own value to an audit trail while the caller received a different one.
    /// </remarks>
    public readonly Task CompleteAsync(
        IMessageDependencies messageDependencies,
        object message,
        IExecutionContext executionContext,
        object? messageResult)
    {
        ArgumentNullException.ThrowIfNull(messageDependencies);
        ArgumentNullException.ThrowIfNull(executionContext);

        return messageDependencies.RunAsyncCompletionHandlers(
            message,
            executionContext,
            Outcome,
            executionContext.MessageResult ?? messageResult,
            Failure,
            Reason,
            Stopwatch.GetElapsedTime(_startedAt));
    }
}
