using System;
using System.Collections.Generic;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     What the decision stages would say about a message, obtained without performing it.
/// </summary>
/// <remarks>
///     <para>
///         Returned by the <c>Evaluate</c> mediator methods. It answers the question a user interface asks before it
///         shows a control: may this caller do this, and is this input well-formed. Asking the pipeline is what stops
///         a second authorization method from drifting away from the one the pipeline uses.
///     </para>
///     <para>
///         It reflects the guard and validator stages only. A shortcut and a pre-handler act rather than decide, so
///         evaluation does not run them, and a message this reports as permitted may still be answered from a cache
///         when it is actually sent.
///     </para>
/// </remarks>
public readonly struct MediationDecision : IEquatable<MediationDecision>
{
    /// <summary>
    ///     The empty failure list shared by every decision that carries none.
    /// </summary>
    private static readonly IReadOnlyList<ValidationFailure> NoFailures = [];

    /// <summary>
    ///     The failures the validator stage collected, or null when it collected none.
    /// </summary>
    private readonly IReadOnlyList<ValidationFailure>? _failures;

    /// <summary>
    ///     Initializes a new instance of the <see cref="MediationDecision" /> struct.
    /// </summary>
    /// <param name="outcome">What the decision stages concluded.</param>
    /// <param name="reason">Why the message would be stopped.</param>
    /// <param name="code">The machine-readable code the decision supplied.</param>
    /// <param name="failures">The failures the validator stage collected.</param>
    private MediationDecision(
        MediationOutcome outcome,
        string? reason,
        string? code,
        IReadOnlyList<ValidationFailure>? failures)
    {
        Outcome = outcome;
        Reason = reason;
        Code = code;
        _failures = failures;
    }

    /// <summary>
    ///     Gets the decision that permits the message to proceed.
    /// </summary>
    /// <value>The default value, so a permitted message allocates nothing.</value>
    public static MediationDecision Allowed => default;

    /// <summary>
    ///     Gets what the decision stages concluded.
    /// </summary>
    /// <value>
    ///     <see cref="MediationOutcome.Succeeded" /> when nothing objected, or
    ///     <see cref="MediationOutcome.Denied" /> or <see cref="MediationOutcome.Invalid" />. No other outcome is
    ///     reachable, because nothing was performed.
    /// </value>
    public MediationOutcome Outcome { get; }

    /// <summary>
    ///     Gets why the message would be stopped.
    /// </summary>
    public string? Reason { get; }

    /// <summary>
    ///     Gets the machine-readable code the decision supplied.
    /// </summary>
    /// <value>
    ///     The code, or <see langword="null" /> when the decision supplied none. It is the same code the message would
    ///     carry if it were sent and refused, so a control and an endpoint can branch on one vocabulary.
    /// </value>
    public string? Code { get; }

    /// <summary>
    ///     Gets the failures the validator stage collected.
    /// </summary>
    public IReadOnlyList<ValidationFailure> Failures => _failures ?? NoFailures;

    /// <summary>
    ///     Gets a value indicating whether nothing objected to the message.
    /// </summary>
    public bool IsAllowed => Outcome == MediationOutcome.Succeeded;

    /// <summary>
    ///     Gets a value indicating whether a guard would refuse the message.
    /// </summary>
    public bool IsDenied => Outcome == MediationOutcome.Denied;

    /// <summary>
    ///     Gets a value indicating whether a validator would report the message malformed.
    /// </summary>
    public bool IsInvalid => Outcome == MediationOutcome.Invalid;

    /// <summary>
    ///     Creates the decision for a message a guard would refuse.
    /// </summary>
    /// <param name="reason">Why the message would be refused.</param>
    /// <param name="code">The code the guard supplied.</param>
    /// <returns>A denied decision.</returns>
    public static MediationDecision Denied(string? reason, string? code)
    {
        return new MediationDecision(MediationOutcome.Denied, reason, code, failures: null);
    }

    /// <summary>
    ///     Creates the decision for a message the validator stage would reject.
    /// </summary>
    /// <param name="reason">The collected failures rendered as one reason.</param>
    /// <param name="code">The code, when a single failure supplied one.</param>
    /// <param name="failures">Every failure the stage collected.</param>
    /// <returns>An invalid decision.</returns>
    public static MediationDecision Invalid(
        string? reason,
        string? code,
        IReadOnlyList<ValidationFailure> failures)
    {
        return new MediationDecision(MediationOutcome.Invalid, reason, code, failures);
    }

    /// <summary>
    ///     Determines whether two decisions are equal.
    /// </summary>
    /// <param name="left">The first decision.</param>
    /// <param name="right">The second decision.</param>
    /// <returns><see langword="true" /> when both carry the same decision.</returns>
    public static bool operator ==(MediationDecision left, MediationDecision right)
    {
        return left.Equals(right);
    }

    /// <summary>
    ///     Determines whether two decisions differ.
    /// </summary>
    /// <param name="left">The first decision.</param>
    /// <param name="right">The second decision.</param>
    /// <returns><see langword="true" /> when they differ.</returns>
    public static bool operator !=(MediationDecision left, MediationDecision right)
    {
        return !left.Equals(right);
    }

    /// <inheritdoc />
    public bool Equals(MediationDecision other)
    {
        return Outcome == other.Outcome
               && string.Equals(Reason, other.Reason, StringComparison.Ordinal)
               && string.Equals(Code, other.Code, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is MediationDecision other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(Outcome, Reason, Code);
    }
}
