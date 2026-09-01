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
///         A refusal is the category holding the two decisions that stop a message without producing a result:
///         <see cref="MediationOutcome.Denied" /> from a guard and <see cref="MediationOutcome.Invalid" /> from the
///         validator stage. It is never an outcome in its own right, and no other outcome can be expressed as one,
///         which is why <see cref="Denied" /> and <see cref="Invalid" /> are the only ways to create one. The two are
///         kept apart because a denial is what a security review reads and a validation failure is not.
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
    /// <remarks>
    ///     Private so that a refusal can only ever carry <see cref="MediationOutcome.Denied" /> or
    ///     <see cref="MediationOutcome.Invalid" />. Create one through <see cref="Denied" /> or <see cref="Invalid" />.
    /// </remarks>
    private Refusal(MediationOutcome outcome, string reason, string? code)
    {
        Outcome = outcome;
        Reason = reason;
        Code = code;
    }

    /// <summary>
    ///     Creates the refusal a guard produces.
    /// </summary>
    /// <param name="reason">Why the message was refused, written for a person.</param>
    /// <param name="code">A machine-readable code a mapper can switch on, when the guard supplied one.</param>
    /// <returns>A refusal reporting <see cref="MediationOutcome.Denied" />.</returns>
    /// <exception cref="ArgumentException"><paramref name="reason" /> is null, empty, or whitespace.</exception>
    public static Refusal Denied(string reason, string? code = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        return new Refusal(MediationOutcome.Denied, reason, code);
    }

    /// <summary>
    ///     Creates the refusal the validator stage produces.
    /// </summary>
    /// <param name="reason">What is wrong with the message, written for a person.</param>
    /// <param name="code">
    ///     A machine-readable code a mapper can switch on. The stage supplies one only when a single validation failure
    ///     was collected, because two failures have no single code.
    /// </param>
    /// <returns>A refusal reporting <see cref="MediationOutcome.Invalid" />.</returns>
    /// <exception cref="ArgumentException"><paramref name="reason" /> is null, empty, or whitespace.</exception>
    public static Refusal Invalid(string reason, string? code = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        return new Refusal(MediationOutcome.Invalid, reason, code);
    }

    /// <summary>
    ///     Gets the outcome the mediation reports for this refusal.
    /// </summary>
    /// <value><see cref="MediationOutcome.Denied" /> for a guard refusal, <see cref="MediationOutcome.Invalid" /> for a
    ///     validation failure.</value>
    public MediationOutcome Outcome { get; }

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
    /// <value>
    ///     <see langword="true" /> when <see cref="Outcome" /> is <see cref="MediationOutcome.Denied" />, and
    ///     <see langword="false" /> when it is <see cref="MediationOutcome.Invalid" />. Those are the only two
    ///     possibilities, so a mapper that handles both cases needs no default branch.
    /// </value>
    public bool IsDenied => Outcome == MediationOutcome.Denied;

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
