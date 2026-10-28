using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     The answer a shortcut returns for a message that produces no result: no shortcut, skip the work because it has
///     already been applied, or deny the message when the refusal can only be decided here.
/// </summary>
/// <remarks>
///     <para>
///         Answering is not a denial. Nothing was refused, the work has already taken effect, and an audit trail
///         records a success. Denying reports <see cref="MediationOutcome.Denied" /> instead, and is reserved for the
///         refusal a guard cannot make because the decision needs the shortcut stage, such as an idempotency claim.
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
    /// <param name="isDenied">Whether the shortcut denied the message.</param>
    /// <param name="reason">The reason the main handler was skipped or the message was denied.</param>
    /// <param name="code">The machine-readable code for the answer or the denial.</param>
    private Shortcut(bool isAnswered, bool isDenied, string? reason, string? code)
    {
        IsAnswered = isAnswered;
        IsDenied = isDenied;
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
    ///     Gets a value indicating whether the shortcut denied the message.
    /// </summary>
    public bool IsDenied { get; }

    /// <summary>
    ///     Gets the reason the main handler was skipped or the message was denied.
    /// </summary>
    /// <remarks>
    ///     A stopped mediation reaches neither post-handlers nor error handlers, so without a reason it leaves no
    ///     explanation anywhere. An answer may omit one, but a denial always carries one.
    /// </remarks>
    public string? Reason { get; }

    /// <summary>
    ///     Gets the machine-readable code for the answer or the denial.
    /// </summary>
    /// <value>
    ///     The code, or <see langword="null" /> when the shortcut supplied none. It means the same thing here as it
    ///     does on <see cref="Verdict.Code" />: something a later stage can switch on, where <see cref="Reason" /> is
    ///     prose written for a person. A completion handler tagging a metric by why the message was answered reads
    ///     this rather than parsing the reason, which is what distinguishes a cache hit from an idempotent replay
    ///     without either shortcut agreeing on wording. A refusal mapper switches on a denial code the same way.
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
        return new Shortcut(isAnswered: true, isDenied: false, reason, code);
    }

    /// <summary>
    ///     Denies the message when the refusal can only be decided at the shortcut stage.
    /// </summary>
    /// <param name="reason">Why the message is denied, written for a person.</param>
    /// <param name="code">A machine-readable code a refusal mapper can switch on, when the shortcut has one.</param>
    /// <returns>A denying shortcut that stops the pipeline and reports <see cref="MediationOutcome.Denied" />.</returns>
    /// <remarks>
    ///     A denial ends the mediation exactly as a guard denial does: completion handlers observe
    ///     <see cref="MediationOutcome.Denied" />, error handlers do not run, and a registered refusal mapper applies.
    ///     Reach for it only when the decision needs the shortcut stage, such as an idempotency claim; anything a
    ///     guard could decide belongs to a guard, which runs first and keeps refusals in one stage.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="reason" /> is null, empty, or whitespace.</exception>
    public static Shortcut Deny(string reason, string? code = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        return new Shortcut(isAnswered: false, isDenied: true, reason, code);
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
               && IsDenied == other.IsDenied
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
        return HashCode.Combine(IsAnswered, IsDenied, Reason, Code);
    }

    /// <summary>
    ///     Converts this shortcut to the decision the pipeline acts on.
    /// </summary>
    /// <param name="decidedBy">The shortcut that produced this answer or denial, recorded so a misuse can be named.</param>
    /// <returns>
    ///     The decision for an answer or a denial, or <see cref="PipelineDecision.Continue" /> when the mediation
    ///     proceeds.
    /// </returns>
    internal PipelineDecision ToDecision(Type decidedBy)
    {
        if (IsDenied)
        {
            return PipelineDecision.Denied(Reason!, Code, decidedBy);
        }

        return IsAnswered
            ? PipelineDecision.Answered(Reason, Code, hasResult: false, result: null, decidedBy)
            : PipelineDecision.Continue;
    }
}
