using System;
using System.Collections.Generic;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     How one mediation ended, as a value the caller branches on rather than an exception it catches.
/// </summary>
/// <remarks>
///     <para>
///         Returned by the <c>Try</c> mediator methods. A denial and a validation failure are routine, expected
///         endings: the pipeline models both as decisions internally, and then the ordinary methods convert them to
///         <see cref="LiteBusMessageDeniedException" /> and <see cref="LiteBusMessageInvalidException" /> at the
///         boundary, which leaves an HTTP endpoint catching an exception to produce a 403. This is the same
///         information without that conversion.
///     </para>
///     <para>
///         A genuine fault still throws. A database timeout is not something a boundary should branch on, and a result
///         carrying the exception would invite one to be swallowed, so the line is drawn where the pipeline already
///         draws it: a decision is a value, a fault is an exception.
///     </para>
///     <para>
///         This is not a replacement for <see cref="IMessageRefusalMapper{TMessage,TMessageResult}" />. A mapper is
///         for an application whose own result type models failure and wants every refusal expressed in it. This is
///         for a caller that wants the framework's answer, and needs no type of its own. Where a mapper is registered,
///         the ordinary method returns the mapped value and reports <see cref="MediationOutcome.Denied" /> here.
///     </para>
/// </remarks>
public readonly struct MediationResult : IEquatable<MediationResult>
{
    /// <summary>
    ///     The empty failure list shared by every result that carries none.
    /// </summary>
    private static readonly IReadOnlyList<ValidationFailure> NoFailures = [];

    /// <summary>
    ///     The validation failures the validator stage collected, or null when it collected none.
    /// </summary>
    private readonly IReadOnlyList<ValidationFailure>? _failures;

    /// <summary>
    ///     Initializes a new instance of the <see cref="MediationResult" /> struct.
    /// </summary>
    /// <param name="outcome">How the mediation ended.</param>
    /// <param name="reason">Why the pipeline stopped, when it stopped.</param>
    /// <param name="code">The machine-readable code the decision supplied.</param>
    /// <param name="failures">The validation failures the validator stage collected.</param>
    private MediationResult(
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
    ///     Gets how the mediation ended.
    /// </summary>
    public MediationOutcome Outcome { get; }

    /// <summary>
    ///     Gets why the pipeline stopped.
    /// </summary>
    /// <value>The reason a guard, validator or shortcut supplied, or <see langword="null" /> when nothing stopped it.</value>
    public string? Reason { get; }

    /// <summary>
    ///     Gets the machine-readable code the decision supplied.
    /// </summary>
    /// <value>
    ///     The code, or <see langword="null" /> when the decision supplied none. Switch on this rather than matching
    ///     <see cref="Reason" />, which is prose written for a person.
    /// </value>
    public string? Code { get; }

    /// <summary>
    ///     Gets the failures the validator stage collected.
    /// </summary>
    /// <value>
    ///     Every failure, or an empty list for any other outcome. The stage collects across every validator, so a
    ///     caller fixing a malformed message sees all of them at once.
    /// </value>
    public IReadOnlyList<ValidationFailure> Failures => _failures ?? NoFailures;

    /// <summary>
    ///     Gets a value indicating whether the message was handled or answered.
    /// </summary>
    /// <value>
    ///     <see langword="true" /> for <see cref="MediationOutcome.Succeeded" /> and
    ///     <see cref="MediationOutcome.Answered" />. An answered message succeeded: the work had already been applied,
    ///     and nothing was refused.
    /// </value>
    public bool IsSuccess => Outcome is MediationOutcome.Succeeded or MediationOutcome.Answered;

    /// <summary>
    ///     Gets a value indicating whether a guard refused the message.
    /// </summary>
    public bool IsDenied => Outcome == MediationOutcome.Denied;

    /// <summary>
    ///     Gets a value indicating whether a validator reported the message malformed.
    /// </summary>
    public bool IsInvalid => Outcome == MediationOutcome.Invalid;

    /// <summary>
    ///     Creates the result for a mediation that ran to completion.
    /// </summary>
    /// <returns>A succeeded result.</returns>
    public static MediationResult Succeeded()
    {
        return new MediationResult(MediationOutcome.Succeeded, reason: null, code: null, failures: null);
    }

    /// <summary>
    ///     Creates the result for a mediation a shortcut answered.
    /// </summary>
    /// <param name="reason">Why the shortcut answered.</param>
    /// <param name="code">The code the shortcut supplied.</param>
    /// <returns>An answered result, which reports <see cref="IsSuccess" />.</returns>
    public static MediationResult Answered(string? reason, string? code)
    {
        return new MediationResult(MediationOutcome.Answered, reason, code, failures: null);
    }

    /// <summary>
    ///     Creates the result for a mediation a guard refused.
    /// </summary>
    /// <param name="reason">Why the message was refused.</param>
    /// <param name="code">The code the guard supplied.</param>
    /// <returns>A denied result.</returns>
    public static MediationResult Denied(string? reason, string? code)
    {
        return new MediationResult(MediationOutcome.Denied, reason, code, failures: null);
    }

    /// <summary>
    ///     Creates the result for a mediation the validator stage rejected.
    /// </summary>
    /// <param name="reason">The collected failures rendered as one reason.</param>
    /// <param name="code">The code, when a single failure supplied one.</param>
    /// <param name="failures">Every failure the stage collected.</param>
    /// <returns>An invalid result.</returns>
    public static MediationResult Invalid(
        string? reason,
        string? code,
        IReadOnlyList<ValidationFailure> failures)
    {
        return new MediationResult(MediationOutcome.Invalid, reason, code, failures);
    }

    /// <summary>
    ///     Determines whether two results are equal.
    /// </summary>
    /// <param name="left">The first result.</param>
    /// <param name="right">The second result.</param>
    /// <returns><see langword="true" /> when both describe the same ending.</returns>
    public static bool operator ==(MediationResult left, MediationResult right)
    {
        return left.Equals(right);
    }

    /// <summary>
    ///     Determines whether two results differ.
    /// </summary>
    /// <param name="left">The first result.</param>
    /// <param name="right">The second result.</param>
    /// <returns><see langword="true" /> when they describe different endings.</returns>
    public static bool operator !=(MediationResult left, MediationResult right)
    {
        return !left.Equals(right);
    }

    /// <inheritdoc />
    public bool Equals(MediationResult other)
    {
        return Outcome == other.Outcome
               && string.Equals(Reason, other.Reason, StringComparison.Ordinal)
               && string.Equals(Code, other.Code, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is MediationResult other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(Outcome, Reason, Code);
    }
}
