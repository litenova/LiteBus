using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     The judgment a guard returns: allow the message to proceed, or refuse it with a reason.
/// </summary>
/// <remarks>
///     <para>
///         The judgment is a return value rather than an exception. That makes it visible in the guard signature, lets
///         the compiler require it, and keeps an expected control-flow path off the exception path.
///     </para>
///     <para>
///         A refusal never owes the caller the result the main handler would have produced, so this shape is correct for
///         every message, including one that produces a result. A guard that would rather hand the caller a refusal
///         value than raise an exception returns <see cref="Verdict{TMessageResult}" /> instead.
///     </para>
/// </remarks>
/// <example>
///     <code><![CDATA[
/// public sealed class RejectClosedAccount : ICommandGuard<WithdrawCommand>
/// {
///     public async Task<Verdict> CheckAsync(
///         WithdrawCommand command,
///         CancellationToken cancellationToken = default)
///     {
///         var account = await _accounts.GetAsync(command.AccountId, cancellationToken);
///
///         return account.IsClosed
///             ? Verdict.Deny("the account is closed")
///             : Verdict.Allow;
///     }
/// }
/// ]]></code>
/// </example>
public readonly struct Verdict : IEquatable<Verdict>
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="Verdict" /> struct.
    /// </summary>
    /// <param name="isDenied">Whether the guard refused the message.</param>
    /// <param name="reason">The reason the message was refused.</param>
    private Verdict(bool isDenied, string? reason)
    {
        IsDenied = isDenied;
        Reason = reason;
    }

    /// <summary>
    ///     Gets a verdict that lets the message proceed.
    /// </summary>
    public static Verdict Allow => default;

    /// <summary>
    ///     Gets a value indicating whether the guard refused the message.
    /// </summary>
    public bool IsDenied { get; }

    /// <summary>
    ///     Gets the reason the message was refused.
    /// </summary>
    /// <remarks>
    ///     A refusal always carries a reason. It reaches completion handlers as
    ///     <see cref="MessageCompletionContext.Reason" /> and an audit trail as the reason on the record, which is the
    ///     one artifact a security review reads.
    /// </remarks>
    public string? Reason { get; }

    /// <summary>
    ///     Creates a verdict that refuses the message.
    /// </summary>
    /// <param name="reason">The reason the message was refused.</param>
    /// <returns>A refusing verdict.</returns>
    /// <remarks>
    ///     The mediation reports <see cref="MessageOutcome.Denied" />, which an audit trail records as a denial, and
    ///     then raises <see cref="LiteBusMessageDeniedException" /> when the caller expects a value, because a refusal
    ///     with no result has nothing to hand back. Supply a value through <see cref="Verdict{TMessageResult}" /> to
    ///     have the caller receive a refusal result instead.
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown when <paramref name="reason" /> is null or whitespace.</exception>
    public static Verdict Deny(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new Verdict(isDenied: true, reason);
    }

    /// <summary>
    ///     Determines whether two verdicts are equal.
    /// </summary>
    /// <param name="left">The first verdict.</param>
    /// <param name="right">The second verdict.</param>
    /// <returns><see langword="true" /> when the verdicts are equal.</returns>
    public static bool operator ==(Verdict left, Verdict right)
    {
        return left.Equals(right);
    }

    /// <summary>
    ///     Determines whether two verdicts differ.
    /// </summary>
    /// <param name="left">The first verdict.</param>
    /// <param name="right">The second verdict.</param>
    /// <returns><see langword="true" /> when the verdicts differ.</returns>
    public static bool operator !=(Verdict left, Verdict right)
    {
        return !left.Equals(right);
    }

    /// <inheritdoc />
    public bool Equals(Verdict other)
    {
        return IsDenied == other.IsDenied && string.Equals(Reason, other.Reason, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is Verdict other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(IsDenied, Reason);
    }

    /// <summary>
    ///     Converts this verdict to the stop the pipeline acts on.
    /// </summary>
    /// <returns>The stop for a refusal, or <see cref="PipelineStop.None" /> when the message may proceed.</returns>
    internal PipelineStop ToStop()
    {
        return IsDenied
            ? PipelineStop.Denied(Reason!, hasResult: false, result: null)
            : PipelineStop.None;
    }
}
