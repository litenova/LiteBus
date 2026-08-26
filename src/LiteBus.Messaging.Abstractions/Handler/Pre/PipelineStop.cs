using System;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Describes a decision stage that ended the mediation before the main handler ran, carrying why it ended and what
///     the caller receives.
/// </summary>
/// <remarks>
///     <para>
///         This is the single currency the pipeline acts on once a <see cref="Verdict" /> or a <see cref="Shortcut" />
///         has been returned. Applications write guards and shortcuts and never construct this type; mediation
///         strategies, including custom ones, read it to learn which outcome to report, what to hand back, and whether
///         a refusal has to surface as an exception because there is nothing to hand back.
///     </para>
///     <para>
///         Keeping the three answers on one type is what stops strategies from drifting apart on the meaning of a
///         refusal.
///     </para>
/// </remarks>
public readonly struct PipelineStop : IEquatable<PipelineStop>
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="PipelineStop" /> struct.
    /// </summary>
    /// <param name="stopsPipeline">Whether the stage ended the mediation.</param>
    /// <param name="outcome">The outcome the mediation reports.</param>
    /// <param name="hasResult">Whether the stage supplied the result the caller receives.</param>
    /// <param name="result">The result returned to the caller.</param>
    /// <param name="reason">The reason the mediation ended.</param>
    private PipelineStop(bool stopsPipeline, MessageOutcome outcome, bool hasResult, object? result, string? reason)
    {
        StopsPipeline = stopsPipeline;
        Outcome = outcome;
        HasResult = hasResult;
        Result = result;
        Reason = reason;
    }

    /// <summary>
    ///     Gets a value indicating that the stage let the mediation proceed.
    /// </summary>
    public static PipelineStop None => default;

    /// <summary>
    ///     Gets a value indicating whether the stage ended the mediation before the main handler ran.
    /// </summary>
    public bool StopsPipeline { get; }

    /// <summary>
    ///     Gets the outcome the mediation reports.
    /// </summary>
    /// <remarks>
    ///     Meaningful only when <see cref="StopsPipeline" /> is <see langword="true" />, where it is either
    ///     <see cref="MessageOutcome.Denied" /> for a refusal or <see cref="MessageOutcome.ShortCircuited" /> for an
    ///     answer that was already known.
    /// </remarks>
    public MessageOutcome Outcome { get; }

    /// <summary>
    ///     Gets a value indicating whether the stage supplied the result the caller receives.
    /// </summary>
    /// <remarks>
    ///     This is distinct from <see cref="Result" /> being <see langword="null" />, because a message whose result type
    ///     is nullable may legitimately be answered with a null result.
    /// </remarks>
    public bool HasResult { get; }

    /// <summary>
    ///     Gets the result returned to the caller.
    /// </summary>
    /// <remarks>
    ///     Meaningful only when <see cref="HasResult" /> is <see langword="true" />. A contract typed over the result is
    ///     the only way to supply one, so the compiler checks that it matches the result type of the message.
    /// </remarks>
    public object? Result { get; }

    /// <summary>
    ///     Gets the reason the mediation ended.
    /// </summary>
    /// <remarks>
    ///     A stopped mediation reaches neither post-handlers nor error handlers, so this reason is the only description
    ///     of why the message ended. It reaches completion handlers as <see cref="MessageCompletionContext.Reason" /> and
    ///     an audit trail as the reason on the record. It is always present on a refusal.
    /// </remarks>
    public string? Reason { get; }

    /// <summary>
    ///     Gets a value indicating whether a refusal supplied no value for the caller to receive.
    /// </summary>
    /// <remarks>
    ///     A refusal in this shape reaches the caller as <see cref="LiteBusMessageDeniedException" />, because a method
    ///     that must return a value has nothing to return.
    /// </remarks>
    public bool IsUnansweredDenial => Outcome == MessageOutcome.Denied && !HasResult;

    /// <summary>
    ///     Determines whether two stops are equal.
    /// </summary>
    /// <param name="left">The first stop.</param>
    /// <param name="right">The second stop.</param>
    /// <returns><see langword="true" /> when the stops are equal.</returns>
    public static bool operator ==(PipelineStop left, PipelineStop right)
    {
        return left.Equals(right);
    }

    /// <summary>
    ///     Determines whether two stops differ.
    /// </summary>
    /// <param name="left">The first stop.</param>
    /// <param name="right">The second stop.</param>
    /// <returns><see langword="true" /> when the stops differ.</returns>
    public static bool operator !=(PipelineStop left, PipelineStop right)
    {
        return !left.Equals(right);
    }

    /// <summary>
    ///     Creates the exception a refusal without a result raises.
    /// </summary>
    /// <param name="messageType">The type of the message that was refused.</param>
    /// <returns>The denial to raise.</returns>
    public LiteBusMessageDeniedException CreateDenial(Type messageType)
    {
        return new LiteBusMessageDeniedException(messageType, Reason ?? "no reason was given");
    }

    /// <summary>
    ///     Reads the result the stage supplied for a message that produces one.
    /// </summary>
    /// <typeparam name="TMessageResult">The result type the caller expects.</typeparam>
    /// <param name="messageType">The concrete runtime type of the message being mediated, used in diagnostics.</param>
    /// <returns>The result the caller receives.</returns>
    /// <exception cref="LiteBusConfigurationException">
    ///     Thrown when a shortcut answered a message that produces a result without supplying one, or supplied one of the
    ///     wrong type.
    /// </exception>
    /// <remarks>
    ///     A shortcut typed over the result type makes both failures impossible, so these branches exist for a shortcut
    ///     written against the untyped contract for a message that does produce a result. Analyzer rule LB1019 reports
    ///     that at build time.
    /// </remarks>
    public TMessageResult ResolveResult<TMessageResult>(Type messageType)
    {
        ArgumentNullException.ThrowIfNull(messageType);

        if (!HasResult)
        {
            throw new LiteBusConfigurationException(
                $"A shortcut answered the mediation of '{messageType.Name}' without supplying the "
                + $"'{typeof(TMessageResult).Name}' the caller expects. Implement "
                + $"IMessageShortcut<{messageType.Name}, {typeof(TMessageResult).Name}> so the compiler requires the "
                + "result, and pass it to Answer.");
        }

        switch (Result)
        {
            case TMessageResult typedResult:
                return typedResult;
            case null:
                return default!;
            default:
                throw new LiteBusConfigurationException(
                    $"A shortcut for '{messageType.Name}' supplied a result of type "
                    + $"'{Result.GetType().Name}', but the message expects '{typeof(TMessageResult).Name}'.");
        }
    }

    /// <inheritdoc />
    public bool Equals(PipelineStop other)
    {
        return StopsPipeline == other.StopsPipeline
               && Outcome == other.Outcome
               && HasResult == other.HasResult
               && Equals(Result, other.Result)
               && string.Equals(Reason, other.Reason, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is PipelineStop other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(StopsPipeline, Outcome, HasResult, Result, Reason);
    }

    /// <summary>
    ///     Creates the stop a refusal produces.
    /// </summary>
    /// <param name="reason">The reason the message was refused.</param>
    /// <param name="hasResult">Whether the guard supplied the value the caller receives.</param>
    /// <param name="result">The value the caller receives, when the guard supplied one.</param>
    /// <returns>The stop the pipeline acts on.</returns>
    internal static PipelineStop Denied(string reason, bool hasResult, object? result)
    {
        return new PipelineStop(stopsPipeline: true, MessageOutcome.Denied, hasResult, result, reason);
    }

    /// <summary>
    ///     Creates the stop an already-known answer produces.
    /// </summary>
    /// <param name="reason">The reason the main handler was skipped.</param>
    /// <param name="hasResult">Whether the shortcut supplied the value the caller receives.</param>
    /// <param name="result">The value the caller receives, when the shortcut supplied one.</param>
    /// <returns>The stop the pipeline acts on.</returns>
    internal static PipelineStop ShortCircuited(string? reason, bool hasResult, object? result)
    {
        return new PipelineStop(stopsPipeline: true, MessageOutcome.ShortCircuited, hasResult, result, reason);
    }
}
