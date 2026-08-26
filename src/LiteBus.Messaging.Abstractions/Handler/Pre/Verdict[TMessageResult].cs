using System;
using System.Collections.Generic;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     The judgment a guard over a message that produces a result returns, able to carry a refusal value the compiler
///     has checked.
/// </summary>
/// <typeparam name="TMessageResult">The result type of the message the guard runs for.</typeparam>
/// <remarks>
///     <para>
///         This shape is opt-in. A refusal does not owe the caller the result the main handler would have produced, so
///         the untyped <see cref="Verdict" /> is correct for a message that produces a result too, and a refusal there
///         reaches the caller as <see cref="LiteBusMessageDeniedException" />. Reach for this shape when the application
///         models failure as a value, so a refusal can be handed back as a failed result object rather than raised.
///     </para>
///     <para>
///         Typing the verdict over the result type is what turns supplying the wrong value from a runtime configuration
///         error into a compile error.
///     </para>
/// </remarks>
/// <example>
///     <code><![CDATA[
/// public sealed class RejectSelfApproval : ICommandGuard<ApproveRefundCommand, Result>
/// {
///     public Task<Verdict<Result>> CheckAsync(
///         ApproveRefundCommand command,
///         CancellationToken cancellationToken = default)
///     {
///         return Task.FromResult(command.ApproverId == command.RequesterId
///             ? Verdict<Result>.Deny("the approver is the requester", Result.Forbidden())
///             : Verdict<Result>.Allow);
///     }
/// }
/// ]]></code>
/// </example>
public readonly struct Verdict<TMessageResult> : IEquatable<Verdict<TMessageResult>>
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="Verdict{TMessageResult}" /> struct.
    /// </summary>
    /// <param name="isDenied">Whether the guard refused the message.</param>
    /// <param name="hasResult">Whether the guard supplied the value the caller receives.</param>
    /// <param name="result">The value the caller receives.</param>
    /// <param name="reason">The reason the message was refused.</param>
    private Verdict(bool isDenied, bool hasResult, TMessageResult? result, string? reason)
    {
        IsDenied = isDenied;
        HasResult = hasResult;
        Result = result;
        Reason = reason;
    }

    /// <summary>
    ///     Gets a verdict that lets the message proceed.
    /// </summary>
    public static Verdict<TMessageResult> Allow => default;

    /// <summary>
    ///     Gets a value indicating whether the guard refused the message.
    /// </summary>
    public bool IsDenied { get; }

    /// <summary>
    ///     Gets a value indicating whether the guard supplied the value the caller receives.
    /// </summary>
    public bool HasResult { get; }

    /// <summary>
    ///     Gets the value the caller receives instead of an exception.
    /// </summary>
    /// <remarks>
    ///     Meaningful only when <see cref="HasResult" /> is <see langword="true" />.
    /// </remarks>
    public TMessageResult? Result { get; }

    /// <summary>
    ///     Gets the reason the message was refused.
    /// </summary>
    public string? Reason { get; }

    /// <summary>
    ///     Creates a verdict that refuses the message and hands the caller a refusal value.
    /// </summary>
    /// <param name="reason">The reason the message was refused.</param>
    /// <param name="result">The value the caller receives, such as a failed result object.</param>
    /// <returns>A refusing verdict that supplies a result.</returns>
    /// <remarks>
    ///     The mediation reports <see cref="MessageOutcome.Denied" />, which an audit trail records as a denial, and
    ///     returns <paramref name="result" /> to the caller without raising an exception.
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown when <paramref name="reason" /> is null or whitespace.</exception>
    public static Verdict<TMessageResult> Deny(string reason, TMessageResult result)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new Verdict<TMessageResult>(isDenied: true, hasResult: true, result, reason);
    }

    /// <summary>
    ///     Creates a verdict that refuses the message without supplying a value.
    /// </summary>
    /// <param name="reason">The reason the message was refused.</param>
    /// <returns>A refusing verdict that supplies no result.</returns>
    /// <remarks>
    ///     The mediation reports <see cref="MessageOutcome.Denied" /> and then raises
    ///     <see cref="LiteBusMessageDeniedException" />, because a refusal with no result has nothing to hand back to a
    ///     caller that expects a value.
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown when <paramref name="reason" /> is null or whitespace.</exception>
    public static Verdict<TMessageResult> Deny(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new Verdict<TMessageResult>(isDenied: true, hasResult: false, result: default, reason);
    }

    /// <summary>
    ///     Determines whether two verdicts are equal.
    /// </summary>
    /// <param name="left">The first verdict.</param>
    /// <param name="right">The second verdict.</param>
    /// <returns><see langword="true" /> when the verdicts are equal.</returns>
    public static bool operator ==(Verdict<TMessageResult> left, Verdict<TMessageResult> right)
    {
        return left.Equals(right);
    }

    /// <summary>
    ///     Determines whether two verdicts differ.
    /// </summary>
    /// <param name="left">The first verdict.</param>
    /// <param name="right">The second verdict.</param>
    /// <returns><see langword="true" /> when the verdicts differ.</returns>
    public static bool operator !=(Verdict<TMessageResult> left, Verdict<TMessageResult> right)
    {
        return !left.Equals(right);
    }

    /// <inheritdoc />
    public bool Equals(Verdict<TMessageResult> other)
    {
        return IsDenied == other.IsDenied
               && HasResult == other.HasResult
               && EqualityComparer<TMessageResult?>.Default.Equals(Result, other.Result)
               && string.Equals(Reason, other.Reason, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is Verdict<TMessageResult> other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(IsDenied, HasResult, Result, Reason);
    }

    /// <summary>
    ///     Converts this verdict to the stop the pipeline acts on.
    /// </summary>
    /// <returns>The stop for a refusal, or <see cref="PipelineStop.None" /> when the message may proceed.</returns>
    internal PipelineStop ToStop()
    {
        return IsDenied
            ? PipelineStop.Denied(Reason!, HasResult, Result)
            : PipelineStop.None;
    }
}
