using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     The answer a shortcut returns for a message that produces no result: no shortcut, or skip the work because it has
///     already been applied.
/// </summary>
/// <remarks>
///     <para>
///         Skipping is not a refusal. Nothing was denied, the work has already taken effect, and an audit trail records
///         a success. Refusing belongs to a guard, which reports <see cref="MediationOutcome.Denied" /> instead.
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
///             ? Shortcut.Skip("the payment was already applied")
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
    private Shortcut(bool isAnswered, string? reason)
    {
        IsAnswered = isAnswered;
        Reason = reason;
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
    ///     Creates a shortcut that skips the main handler because the work has already been applied.
    /// </summary>
    /// <param name="reason">The reason the main handler was skipped.</param>
    /// <returns>An answering shortcut that supplies no result.</returns>
    /// <remarks>
    ///     The mediation reports <see cref="MediationOutcome.Answered" />, and an audit trail records a success,
    ///     because nothing was refused.
    /// </remarks>
    public static Shortcut Skip(string? reason = null)
    {
        return new Shortcut(isAnswered: true, reason);
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
        return IsAnswered == other.IsAnswered && string.Equals(Reason, other.Reason, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is Shortcut other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(IsAnswered, Reason);
    }

    /// <summary>
    ///     Converts this shortcut to the stop the pipeline acts on.
    /// </summary>
    /// <returns>The stop for an answer, or <see cref="PipelineStop.None" /> when the mediation proceeds.</returns>
    internal PipelineStop ToStop()
    {
        return IsAnswered
            ? PipelineStop.Answered(Reason, hasResult: false, result: null)
            : PipelineStop.None;
    }
}
