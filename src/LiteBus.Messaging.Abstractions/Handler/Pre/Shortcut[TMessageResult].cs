using System;
using System.Collections.Generic;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     The answer a shortcut returns for a message that produces a result: here is the result, or carry on.
/// </summary>
/// <typeparam name="TMessageResult">The type of result the message produces.</typeparam>
/// <remarks>
///     <para>
///         <see cref="None" /> is the default, so a shortcut that has no answer returns it without allocating.
///     </para>
///     <para>
///         Answering always supplies the result, because a shortcut is standing in for the main handler and the caller
///         is owed the value that handler would have produced. There is no way to answer a result-returning message
///         without one; a stream query that means "no items" answers with an empty sequence, which says the same thing
///         explicitly:
///     </para>
///     <code>
///     Shortcut&lt;IAsyncEnumerable&lt;Product&gt;&gt;.Answer(AsyncEnumerable.Empty&lt;Product&gt;())
///     </code>
///     <para>
///         Answering reports <see cref="MediationOutcome.Answered" />, which an audit trail records as a success, because
///         nothing was denied. Denying is a guard's job and reports <see cref="MediationOutcome.Denied" />.
///     </para>
/// </remarks>
public readonly struct Shortcut<TMessageResult> : IEquatable<Shortcut<TMessageResult>>
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="Shortcut{TMessageResult}" /> struct.
    /// </summary>
    /// <param name="isAnswered">Whether the shortcut answered the message.</param>
    /// <param name="result">The result the shortcut supplied.</param>
    /// <param name="reason">Why the shortcut answered.</param>
    private Shortcut(bool isAnswered, TMessageResult? result, string? reason)
    {
        IsAnswered = isAnswered;
        Result = result;
        Reason = reason;
    }

    /// <summary>
    ///     Gets the answer a shortcut returns when it has none, letting the mediation proceed.
    /// </summary>
    /// <value>The default value, so returning it allocates nothing.</value>
    public static Shortcut<TMessageResult> None => default;

    /// <summary>
    ///     Gets a value indicating whether the shortcut answered the message.
    /// </summary>
    /// <value><see langword="true" /> when the main handler must not run.</value>
    public bool IsAnswered { get; }

    /// <summary>
    ///     Gets the result the shortcut supplied.
    /// </summary>
    /// <value>
    ///     The value the caller receives when <see cref="IsAnswered" /> is <see langword="true" />. A shortcut may answer
    ///     with <see langword="null" /> when the result type is nullable.
    /// </value>
    public TMessageResult? Result { get; }

    /// <summary>
    ///     Gets the reason the shortcut answered.
    /// </summary>
    /// <value>
    ///     The reason, which reaches completion handlers and the audit trail, or <see langword="null" /> when the
    ///     shortcut gave none.
    /// </value>
    public string? Reason { get; }

    /// <summary>
    ///     Answers the message with the given result, so the main handler never runs.
    /// </summary>
    /// <param name="result">The value the caller receives in place of the one the main handler would have produced.</param>
    /// <param name="reason">Why the answer was already known, recorded by completion handlers and the audit trail.</param>
    /// <returns>A shortcut that stops the pipeline and reports <see cref="MediationOutcome.Answered" />.</returns>
    public static Shortcut<TMessageResult> Answer(TMessageResult result, string? reason = null)
    {
        return new Shortcut<TMessageResult>(isAnswered: true, result, reason);
    }

    /// <summary>
    ///     Determines whether two shortcuts are equal.
    /// </summary>
    /// <param name="left">The first shortcut.</param>
    /// <param name="right">The second shortcut.</param>
    /// <returns><see langword="true" /> when both carry the same answer.</returns>
    public static bool operator ==(Shortcut<TMessageResult> left, Shortcut<TMessageResult> right)
    {
        return left.Equals(right);
    }

    /// <summary>
    ///     Determines whether two shortcuts differ.
    /// </summary>
    /// <param name="left">The first shortcut.</param>
    /// <param name="right">The second shortcut.</param>
    /// <returns><see langword="true" /> when they carry different answers.</returns>
    public static bool operator !=(Shortcut<TMessageResult> left, Shortcut<TMessageResult> right)
    {
        return !left.Equals(right);
    }

    /// <inheritdoc />
    public bool Equals(Shortcut<TMessageResult> other)
    {
        return IsAnswered == other.IsAnswered
               && EqualityComparer<TMessageResult?>.Default.Equals(Result, other.Result)
               && string.Equals(Reason, other.Reason, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is Shortcut<TMessageResult> other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(IsAnswered, Result, Reason);
    }

    /// <summary>
    ///     Converts this shortcut to the pipeline decision the stage runner acts on.
    /// </summary>
    /// <param name="answeredBy">The shortcut that produced this answer, recorded so a misuse can be named.</param>
    /// <returns>
    ///     A decision carrying the result when the shortcut answered, otherwise <see cref="PipelineDecision.Continue" />.
    /// </returns>
    internal PipelineDecision ToDecision(Type answeredBy)
    {
        return IsAnswered
            ? PipelineDecision.Answered(Reason, hasResult: true, Result, answeredBy)
            : PipelineDecision.Continue;
    }
}
