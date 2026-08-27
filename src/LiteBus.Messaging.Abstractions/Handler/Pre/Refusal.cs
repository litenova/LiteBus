using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Describes why a pre-stage decision refused a message.
/// </summary>
/// <remarks>
///     <para>
///         A refusal is handed to <see cref="IMessageRefusalMapper{TMessage,TMessageResult}" /> so an application that
///         models failure as a value can turn the decision into the type the caller expects. The mapper is the only
///         place that translation lives, so every guard and validator for a message shares one definition of what a
///         refusal looks like to a caller.
///     </para>
///     <para>
///         <see cref="Outcome" /> is either <see cref="MessageOutcome.Denied" /> for a guard refusal or
///         <see cref="MessageOutcome.Invalid" /> for a validation failure. The two are kept apart because a denial is
///         what a security review reads and a validation failure is not.
///     </para>
/// </remarks>
public readonly struct Refusal : IEquatable<Refusal>
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="Refusal" /> struct.
    /// </summary>
    /// <param name="outcome">The outcome the mediation reports.</param>
    /// <param name="reason">The reason the decision supplied.</param>
    /// <param name="code">The optional code the decision supplied.</param>
    public Refusal(MessageOutcome outcome, string reason, string? code = null)
    {
        Outcome = outcome;
        Reason = reason;
        Code = code;
    }

    /// <summary>
    ///     Gets the outcome the mediation reports for this refusal.
    /// </summary>
    /// <value><see cref="MessageOutcome.Denied" /> for a guard refusal, <see cref="MessageOutcome.Invalid" /> for a
    ///     validation failure.</value>
    public MessageOutcome Outcome { get; }

    /// <summary>
    ///     Gets the reason the decision supplied.
    /// </summary>
    /// <value>The human-readable reason, which a refusal always carries.</value>
    public string Reason { get; }

    /// <summary>
    ///     Gets the code the decision supplied, when any.
    /// </summary>
    /// <value>
    ///     The machine-readable code, or <see langword="null" /> when the decision supplied none. A mapper switches on
    ///     this rather than parsing <see cref="Reason" />, which is prose written for a person.
    /// </value>
    public string? Code { get; }

    /// <summary>
    ///     Gets a value indicating whether a guard refused the message.
    /// </summary>
    /// <value><see langword="true" /> when <see cref="Outcome" /> is <see cref="MessageOutcome.Denied" />.</value>
    public bool IsDenial => Outcome == MessageOutcome.Denied;

    /// <summary>
    ///     Determines whether two refusals are equal.
    /// </summary>
    /// <param name="left">The first refusal.</param>
    /// <param name="right">The second refusal.</param>
    /// <returns><see langword="true" /> when both describe the same refusal.</returns>
    public static bool operator ==(Refusal left, Refusal right)
    {
        return left.Equals(right);
    }

    /// <summary>
    ///     Determines whether two refusals differ.
    /// </summary>
    /// <param name="left">The first refusal.</param>
    /// <param name="right">The second refusal.</param>
    /// <returns><see langword="true" /> when they describe different refusals.</returns>
    public static bool operator !=(Refusal left, Refusal right)
    {
        return !left.Equals(right);
    }

    /// <inheritdoc />
    public bool Equals(Refusal other)
    {
        return Outcome == other.Outcome
               && string.Equals(Reason, other.Reason, StringComparison.Ordinal)
               && string.Equals(Code, other.Code, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is Refusal other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(Outcome, Reason, Code);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return Code is null ? $"{Outcome}: {Reason}" : $"{Outcome} [{Code}]: {Reason}";
    }
}
