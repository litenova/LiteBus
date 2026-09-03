using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     The answer a shortcut returns for a message that produces no result: no shortcut, or skip the work because it has
///     already been applied.
/// </summary>
/// <remarks>
///     <para>
///         Answering is not a denial. Nothing was refused, the work has already taken effect, and an audit trail
///         records a success. Denying belongs to a guard, which reports <see cref="MediationOutcome.Denied" /> instead.
///     </para>
///     <para>
///         This shape is for messages that produce no result. Use <see cref="Shortcut{TMessageResult}" /> for a message
///         that produces one, so the compiler checks the value the shortcut supplies.
///     </para>
///     <para>
///         Answering belongs to the shortcut stage alone, because answering means skipping the work. Once the main
///         handler has run there is nothing left to skip; a handler that wants to suppress the reactions to a no-op
///         calls <see cref="IExecutionContext.SuppressPostHandlers" /> instead.
///     </para>
/// </remarks>
/// <example>
///     <code><![CDATA[
/// public sealed class SkipAppliedPayment : ICommandShortcut<ProcessPaymentCommand>
/// {
///     public async Task<Shortcut> TryAnswerAsync(
///         ProcessPaymentCommand command,
///         CancellationToken cancellationToken = default)
///     {
///         return await _ledger.AlreadyAppliedAsync(command.PaymentId, cancellationToken)
///             ? Shortcut.Answer("the payment was already applied")
///             : Shortcut.None;
///     }
/// }
/// ]]></code>
/// </example>
public readonly struct Shortcut : IEquatable<Shortcut>
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="Shortcut" /> struct.
    /// </summary>
    /// <param name="isAnswered">Whether the shortcut skipped the main handler.</param>
    /// <param name="reason">The reason the main handler was skipped.</param>
    /// <param name="code">The machine-readable code for the answer.</param>
    private Shortcut(bool isAnswered, string? reason, string? code)
    {
        IsAnswered = isAnswered;
        Reason = reason;
        Code = code;
    }

    /// <summary>
    ///     Gets a shortcut that supplies no answer, so the mediation proceeds.
    /// </summary>
    public static Shortcut None => default;

    /// <summary>
    ///     Gets a value indicating whether the shortcut answered for the main handler.
    /// </summary>
    public bool IsAnswered { get; }

    /// <summary>
    ///     Gets the reason the main handler was skipped.
    /// </summary>
    /// <remarks>
    ///     A skipped mediation reaches neither post-handlers nor error handlers, so without a reason it leaves no
    ///     explanation anywhere. Supply one.
    /// </remarks>
    public string? Reason { get; }

    /// <summary>
    ///     Gets the machine-readable code for the answer.
    /// </summary>
    /// <value>
    ///     The code, or <see langword="null" /> when the shortcut supplied none. It means the same thing here as it
    ///     does on <see cref="Verdict.Code" />: something a later stage can switch on, where <see cref="Reason" /> is
    ///     prose written for a person. A completion handler tagging a metric by why the message was answered reads
    ///     this rather than parsing the reason, which is what distinguishes a cache hit from an idempotent replay
    ///     without either shortcut agreeing on wording.
    /// </value>
    public string? Code { get; }

    /// <summary>
    ///     Answers the message because the work has already been applied, so the main handler never runs.
    /// </summary>
    /// <param name="reason">The reason the main handler was skipped.</param>
    /// <param name="code">A machine-readable code a completion handler or a metric can switch on.</param>
    /// <returns>An answering shortcut that supplies no result.</returns>
    /// <remarks>
    ///     The mediation reports <see cref="MediationOutcome.Answered" />, and an audit trail records a success,
    ///     because nothing was denied. The verb matches <see cref="Shortcut{TMessageResult}.Answer" /> so that both
    ///     shapes of shortcut read the same way; this one takes no result because the message produces none.
    /// </remarks>
    public static Shortcut Answer(string? reason = null, string? code = null)
    {
        return new Shortcut(isAnswered: true, reason, code);
    }

    /// <summary>
    ///     Determines whether two shortcuts are equal.
    /// </summary>
    /// <param name="left">The first shortcut.</param>
    /// <param name="right">The second shortcut.</param>
    /// <returns><see langword="true" /> when the shortcuts are equal.</returns>
    public static bool operator ==(Shortcut left, Shortcut right)
    {
        return left.Equals(right);
    }

    /// <summary>
    ///     Determines whether two shortcuts differ.
    /// </summary>
    /// <param name="left">The first shortcut.</param>
    /// <param name="right">The second shortcut.</param>
    /// <returns><see langword="true" /> when the shortcuts differ.</returns>
    public static bool operator !=(Shortcut left, Shortcut right)
    {
        return !left.Equals(right);
    }

    /// <inheritdoc />
    public bool Equals(Shortcut other)
    {
        return IsAnswered == other.IsAnswered
               && string.Equals(Reason, other.Reason, StringComparison.Ordinal)
               && string.Equals(Code, other.Code, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is Shortcut other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(IsAnswered, Reason, Code);
    }

    /// <summary>
    ///     Converts this shortcut to the decision the pipeline acts on.
    /// </summary>
    /// <param name="answeredBy">The shortcut that produced this answer, recorded so a misuse can be named.</param>
    /// <returns>The decision for an answer, or <see cref="PipelineDecision.Continue" /> when the mediation proceeds.</returns>
    internal PipelineDecision ToDecision(Type answeredBy)
    {
        return IsAnswered
            ? PipelineDecision.Answered(Reason, Code, hasResult: false, result: null, answeredBy)
            : PipelineDecision.Continue;
    }
}
