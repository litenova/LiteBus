using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     The answer a guard returns: the message may proceed, or it is refused and here is why.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="Allow" /> is the default, so a guard that permits the message returns it without allocating.
///     </para>
///     <para>
///         A verdict never carries a result. A refusal does not owe the caller the value the main handler would have
///         produced, which is why one guard contract fits every message, whether or not it produces a result. An
///         application that returns a failed result object instead of raising registers an
///         <see cref="IMessageRefusalMapper{TMessage,TMessageResult}" />, so the shape of a refused result is defined
///         once for the message rather than in each guard.
///     </para>
///     <para>
///         Example:
///     </para>
///     <code>
///     public Task&lt;Verdict&gt; DecideAsync(
///         TransferFunds message,
///         CancellationToken cancellationToken = default)
///     {
///         return Task.FromResult(message.Amount > Threshold
///             ? Verdict.Deny("transfers above the threshold need a second approver", code: "SECOND_APPROVER")
///             : Verdict.Allow);
///     }
///     </code>
/// </remarks>
public readonly struct Verdict : IEquatable<Verdict>
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="Verdict" /> struct.
    /// </summary>
    /// <param name="isDenied">Whether the guard refused the message.</param>
    /// <param name="reason">The reason for the refusal.</param>
    /// <param name="code">The code for the refusal.</param>
    private Verdict(bool isDenied, string? reason, string? code)
    {
        IsDenied = isDenied;
        Reason = reason;
        Code = code;
    }

    /// <summary>
    ///     Gets the verdict that permits the message to proceed.
    /// </summary>
    /// <value>The default value, so returning it allocates nothing.</value>
    public static Verdict Allow => default;

    /// <summary>
    ///     Gets a value indicating whether the guard refused the message.
    /// </summary>
    /// <value><see langword="true" /> when the message is refused.</value>
    public bool IsDenied { get; }

    /// <summary>
    ///     Gets the reason for the refusal.
    /// </summary>
    /// <value>The reason, which a refusal always carries, or <see langword="null" /> when the message is permitted.</value>
    public string? Reason { get; }

    /// <summary>
    ///     Gets the code for the refusal.
    /// </summary>
    /// <value>
    ///     The machine-readable code, or <see langword="null" /> when the guard supplied none. A refusal mapper switches
    ///     on this rather than parsing <see cref="Reason" />, which is prose written for a person.
    /// </value>
    public string? Code { get; }

    /// <summary>
    ///     Refuses the message.
    /// </summary>
    /// <param name="reason">Why the message is refused, written for a person.</param>
    /// <param name="code">A machine-readable code a refusal mapper can switch on, when the guard has one.</param>
    /// <returns>A verdict that stops the pipeline and reports <see cref="MessageOutcome.Denied" />.</returns>
    /// <exception cref="ArgumentException"><paramref name="reason" /> is null, empty, or whitespace.</exception>
    public static Verdict Deny(string reason, string? code = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        return new Verdict(isDenied: true, reason, code);
    }

    /// <summary>
    ///     Determines whether two verdicts are equal.
    /// </summary>
    /// <param name="left">The first verdict.</param>
    /// <param name="right">The second verdict.</param>
    /// <returns><see langword="true" /> when both carry the same decision.</returns>
    public static bool operator ==(Verdict left, Verdict right)
    {
        return left.Equals(right);
    }

    /// <summary>
    ///     Determines whether two verdicts differ.
    /// </summary>
    /// <param name="left">The first verdict.</param>
    /// <param name="right">The second verdict.</param>
    /// <returns><see langword="true" /> when they carry different decisions.</returns>
    public static bool operator !=(Verdict left, Verdict right)
    {
        return !left.Equals(right);
    }

    /// <inheritdoc />
    public bool Equals(Verdict other)
    {
        return IsDenied == other.IsDenied
               && string.Equals(Reason, other.Reason, StringComparison.Ordinal)
               && string.Equals(Code, other.Code, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is Verdict other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(IsDenied, Reason, Code);
    }

    /// <summary>
    ///     Converts this verdict to the pipeline decision the stage runner acts on.
    /// </summary>
    /// <returns>
    ///     A stop that reports <see cref="MessageOutcome.Denied" /> when the guard refused, otherwise
    ///     <see cref="PipelineStop.None" />.
    /// </returns>
    internal PipelineStop ToStop()
    {
        return IsDenied ? PipelineStop.Denied(Reason!, Code) : PipelineStop.None;
    }
}
