using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     How a mediation ended: which outcome, what failed, and why it stopped.
/// </summary>
/// <remarks>
///     <para>
///         Three values always travel together from wherever a mediation ends to the completion stage that reports it.
///         Every strategy used to carry them as three separate locals and assemble them in a <c>finally</c>, which is
///         three chances to record one and forget another.
///     </para>
///     <para>
///         This is a value, not a tracker. Each transition returns a new ending rather than mutating one, so a strategy
///         reassigns as it goes and there is no mutable struct to pass carefully. Running the error stage and the
///         completion stage are separate concerns and live with the other stage runners on
///         <see cref="MessageContextExtensions" />.
///     </para>
///     <para>
///         A custom mediation strategy should carry one of these and hand it to
///         <see cref="MessageContextExtensions.RunAsyncCompletionHandlers(IMessageDependencies,object,IExecutionContext,MediationEnding,object?,TimeSpan)" />
///         in a <c>finally</c>.
///     </para>
/// </remarks>
public readonly struct MediationEnding : IEquatable<MediationEnding>
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="MediationEnding" /> struct.
    /// </summary>
    /// <param name="outcome">The outcome the mediation reports.</param>
    /// <param name="failure">The exception that ended the mediation, when one did.</param>
    /// <param name="reason">The reason a pre-stage decision gave for stopping.</param>
    private MediationEnding(MessageOutcome outcome, Exception? failure, string? reason)
    {
        Outcome = outcome;
        Failure = failure;
        Reason = reason;
    }

    /// <summary>
    ///     Gets the ending a mediation is assumed to have until something records otherwise.
    /// </summary>
    /// <value>
    ///     The default value, because <see cref="MessageOutcome.Succeeded" /> is zero: a mediation that runs to the end
    ///     of its own body without a decision or a fault has succeeded, and saying so costs nothing.
    /// </value>
    public static MediationEnding Succeeded => default;

    /// <summary>
    ///     Gets how the mediation ended.
    /// </summary>
    public MessageOutcome Outcome { get; }

    /// <summary>
    ///     Gets the exception that ended the mediation, when one did.
    /// </summary>
    /// <value>The fault, the cancellation, or the refusal that reached the caller; otherwise <see langword="null" />.</value>
    public Exception? Failure { get; }

    /// <summary>
    ///     Gets the reason a pre-stage decision gave for stopping the pipeline.
    /// </summary>
    /// <value>The reason, or <see langword="null" /> when no decision stopped the mediation.</value>
    public string? Reason { get; }

    /// <summary>
    ///     Determines whether two endings are equal.
    /// </summary>
    /// <param name="left">The first ending.</param>
    /// <param name="right">The second ending.</param>
    /// <returns><see langword="true" /> when both describe the same ending.</returns>
    public static bool operator ==(MediationEnding left, MediationEnding right)
    {
        return left.Equals(right);
    }

    /// <summary>
    ///     Determines whether two endings differ.
    /// </summary>
    /// <param name="left">The first ending.</param>
    /// <param name="right">The second ending.</param>
    /// <returns><see langword="true" /> when they describe different endings.</returns>
    public static bool operator !=(MediationEnding left, MediationEnding right)
    {
        return !left.Equals(right);
    }

    /// <summary>
    ///     The ending a pre-stage decision produced.
    /// </summary>
    /// <param name="stop">The decision a guard, validator, or shortcut returned.</param>
    /// <returns>An ending carrying that decision's outcome and reason.</returns>
    public MediationEnding Stopped(PipelineStop stop)
    {
        return new MediationEnding(stop.Outcome, Failure, stop.Reason);
    }

    /// <summary>
    ///     The same ending, carrying the exception a refusal reached the caller as.
    /// </summary>
    /// <param name="refusal">The denial or invalid-message exception.</param>
    /// <returns>An ending with the refusal attached and the outcome unchanged.</returns>
    /// <remarks>
    ///     The outcome stays <see cref="MessageOutcome.Denied" /> or <see cref="MessageOutcome.Invalid" /> from
    ///     <see cref="Stopped" />. A refusal reaching the caller as an exception is still a decision rather than a fault,
    ///     so it must not become <see cref="MessageOutcome.Failed" />.
    /// </remarks>
    public MediationEnding Refused(Exception refusal)
    {
        return new MediationEnding(Outcome, refusal, Reason);
    }

    /// <summary>
    ///     The ending a cancellation produced.
    /// </summary>
    /// <param name="cancellation">The cancellation that ended the mediation.</param>
    /// <returns>An ending reporting <see cref="MessageOutcome.Canceled" />.</returns>
    public MediationEnding Canceled(OperationCanceledException cancellation)
    {
        return new MediationEnding(MessageOutcome.Canceled, cancellation, Reason);
    }

    /// <summary>
    ///     The ending a fault produced.
    /// </summary>
    /// <param name="failure">The exception raised somewhere in the pipeline.</param>
    /// <returns>An ending reporting <see cref="MessageOutcome.Failed" />.</returns>
    public MediationEnding Faulted(Exception failure)
    {
        return new MediationEnding(MessageOutcome.Failed, failure, Reason);
    }

    /// <inheritdoc />
    public bool Equals(MediationEnding other)
    {
        return Outcome == other.Outcome
               && ReferenceEquals(Failure, other.Failure)
               && string.Equals(Reason, other.Reason, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is MediationEnding other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(Outcome, Failure, Reason);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return Reason is null ? Outcome.ToString() : $"{Outcome}: {Reason}";
    }
}
