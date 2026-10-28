using System;
using System.Collections.Generic;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     How one mediation of a result-producing message ended, carrying the value when it produced one.
/// </summary>
/// <typeparam name="TMessageResult">The type of result the message produces.</typeparam>
/// <remarks>
///     <para>
///         Returned by the <c>Try</c> mediator methods for a message that produces a result. Read
///         <see cref="IsSuccess" /> before <see cref="Value" />: a refused mediation produces nothing, because a
///         refusal does not owe the caller the value the main handler would have produced.
///     </para>
///     <para>
///         Where an <see cref="IMessageRefusalMapper{TMessage,TMessageResult}" /> is registered, a refusal arrives here
///         with the mapped value in <see cref="Value" /> and <see cref="MediationOutcome.Denied" /> as the outcome, so
///         an application that models failure in its own result type sees both its value and the framework's
///         classification.
///     </para>
/// </remarks>
public readonly struct MediationResult<TMessageResult> : IEquatable<MediationResult<TMessageResult>>
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
    ///     Initializes a new instance of the <see cref="MediationResult{TMessageResult}" /> struct.
    /// </summary>
    /// <param name="outcome">How the mediation ended.</param>
    /// <param name="value">The value the caller receives, when there is one.</param>
    /// <param name="hasValue">Whether a value was produced.</param>
    /// <param name="reason">Why the pipeline stopped, when it stopped.</param>
    /// <param name="code">The machine-readable code the decision supplied.</param>
    /// <param name="failures">The validation failures the validator stage collected.</param>
    private MediationResult(
        MediationOutcome outcome,
        TMessageResult? value,
        bool hasValue,
        string? reason,
        string? code,
        IReadOnlyList<ValidationFailure>? failures)
    {
        Outcome = outcome;
        Value = value;
        HasValue = hasValue;
        Reason = reason;
        Code = code;
        _failures = failures;
    }

    /// <summary>
    ///     Gets how the mediation ended.
    /// </summary>
    public MediationOutcome Outcome { get; }

    /// <summary>
    ///     Gets the value the caller receives.
    /// </summary>
    /// <value>
    ///     The value the main handler produced, the value a shortcut answered with, the value a refusal mapper
    ///     supplied, or the default when nothing produced one. Test <see cref="HasValue" /> to tell a produced
    ///     <see langword="null" /> apart from no value at all.
    /// </value>
    public TMessageResult? Value { get; }

    /// <summary>
    ///     Gets a value indicating whether the mediation produced a value.
    /// </summary>
    /// <value>
    ///     <see langword="false" /> for a refusal with no registered mapper. It is separate from
    ///     <see cref="IsSuccess" /> because a nullable result type makes <see cref="Value" /> ambiguous on its own.
    /// </value>
    public bool HasValue { get; }

    /// <summary>
    ///     Gets why the pipeline stopped.
    /// </summary>
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
    public IReadOnlyList<ValidationFailure> Failures => _failures ?? NoFailures;

    /// <summary>
    ///     Gets a value indicating whether the message was handled or answered.
    /// </summary>
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
    ///     Creates the result for a mediation that produced a value.
    /// </summary>
    /// <param name="value">The value the caller receives.</param>
    /// <returns>A succeeded result carrying the value.</returns>
    public static MediationResult<TMessageResult> Succeeded(TMessageResult value)
    {
        return new MediationResult<TMessageResult>(
            MediationOutcome.Succeeded, value, hasValue: true, reason: null, code: null, failures: null);
    }

    /// <summary>
    ///     Creates the result for a mediation a shortcut answered.
    /// </summary>
    /// <param name="value">The value the shortcut supplied.</param>
    /// <param name="reason">Why the shortcut answered.</param>
    /// <param name="code">The code the shortcut supplied.</param>
    /// <returns>An answered result carrying the value.</returns>
    public static MediationResult<TMessageResult> Answered(TMessageResult value, string? reason, string? code)
    {
        return new MediationResult<TMessageResult>(
            MediationOutcome.Answered, value, hasValue: true, reason, code, failures: null);
    }

    /// <summary>
    ///     Creates the result for a mediation a guard refused.
    /// </summary>
    /// <param name="reason">Why the message was refused.</param>
    /// <param name="code">The code the guard supplied.</param>
    /// <param name="value">The value a refusal mapper supplied, when one is registered.</param>
    /// <param name="hasValue">Whether a refusal mapper supplied a value.</param>
    /// <returns>A denied result.</returns>
    public static MediationResult<TMessageResult> Denied(
        string? reason,
        string? code,
        TMessageResult? value = default,
        bool hasValue = false)
    {
        return new MediationResult<TMessageResult>(
            MediationOutcome.Denied, value, hasValue, reason, code, failures: null);
    }

    /// <summary>
    ///     Creates the result for a mediation the validator stage rejected.
    /// </summary>
    /// <param name="reason">The collected failures rendered as one reason.</param>
    /// <param name="code">The code, when a single failure supplied one.</param>
    /// <param name="failures">Every failure the stage collected.</param>
    /// <param name="value">The value a refusal mapper supplied, when one is registered.</param>
    /// <param name="hasValue">Whether a refusal mapper supplied a value.</param>
    /// <returns>An invalid result.</returns>
    public static MediationResult<TMessageResult> Invalid(
        string? reason,
        string? code,
        IReadOnlyList<ValidationFailure> failures,
        TMessageResult? value = default,
        bool hasValue = false)
    {
        return new MediationResult<TMessageResult>(
            MediationOutcome.Invalid, value, hasValue, reason, code, failures);
    }

    /// <summary>
    ///     Determines whether two results are equal.
    /// </summary>
    /// <param name="left">The first result.</param>
    /// <param name="right">The second result.</param>
    /// <returns><see langword="true" /> when both describe the same ending and value.</returns>
    public static bool operator ==(MediationResult<TMessageResult> left, MediationResult<TMessageResult> right)
    {
        return left.Equals(right);
    }

    /// <summary>
    ///     Determines whether two results differ.
    /// </summary>
    /// <param name="left">The first result.</param>
    /// <param name="right">The second result.</param>
    /// <returns><see langword="true" /> when they differ.</returns>
    public static bool operator !=(MediationResult<TMessageResult> left, MediationResult<TMessageResult> right)
    {
        return !left.Equals(right);
    }

    /// <inheritdoc />
    public bool Equals(MediationResult<TMessageResult> other)
    {
        return Outcome == other.Outcome
               && HasValue == other.HasValue
               && EqualityComparer<TMessageResult?>.Default.Equals(Value, other.Value)
               && string.Equals(Reason, other.Reason, StringComparison.Ordinal)
               && string.Equals(Code, other.Code, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is MediationResult<TMessageResult> other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(Outcome, HasValue, Value, Reason, Code);
    }
}
