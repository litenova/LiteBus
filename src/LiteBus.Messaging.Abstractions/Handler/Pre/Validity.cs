using System;
using System.Collections.Generic;
using System.Linq;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     The answer a validator returns: the message is well-formed, or it is not and here is why.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="Valid" /> is the default, so a validator that finds nothing wrong returns it without allocating.
///     </para>
///     <para>
///         Validity is separate from <see cref="Verdict" /> because the two answer different questions. A guard answers
///         "may this happen" and a refusal is security-relevant. A validator answers "is this well-formed" and a failure
///         is not. The mediation reports <see cref="MediationOutcome.Invalid" /> rather than
///         <see cref="MediationOutcome.Denied" />, and an audit trail keeps them apart for the same reason.
///     </para>
///     <para>
///         The validator stage collects failures from every validator rather than stopping at the first, so a caller
///         fixing a malformed message sees all of them at once. That is the one way this stage differs from the guard
///         stage, which stops at the first refusal because one reason is enough.
///     </para>
/// </remarks>
public readonly struct Validity : IEquatable<Validity>
{
    /// <summary>
    ///     The empty list returned for a well-formed message, shared so reading it allocates nothing.
    /// </summary>
    private static readonly IReadOnlyList<ValidationFailure> NoFailures = [];

    /// <summary>
    ///     The failures the validator reported, or null when it reported none.
    /// </summary>
    private readonly IReadOnlyList<ValidationFailure>? _failures;

    /// <summary>
    ///     Initializes a new instance of the <see cref="Validity" /> struct.
    /// </summary>
    /// <param name="failures">The failures the validator found, or <see langword="null" /> when it found none.</param>
    private Validity(IReadOnlyList<ValidationFailure>? failures)
    {
        _failures = failures;
    }

    /// <summary>
    ///     Gets the answer a validator returns when the message is well-formed.
    /// </summary>
    /// <value>The default value, so returning it allocates nothing.</value>
    public static Validity Valid => default;

    /// <summary>
    ///     Gets a value indicating whether the validator found the message malformed.
    /// </summary>
    /// <value><see langword="true" /> when at least one failure was reported.</value>
    public bool IsInvalid => _failures is { Count: > 0 };

    /// <summary>
    ///     Gets the failures the validator reported.
    /// </summary>
    /// <value>The failures, or an empty list when the message is well-formed.</value>
    public IReadOnlyList<ValidationFailure> Failures => _failures ?? NoFailures;

    /// <summary>
    ///     Reports the message malformed for a single reason.
    /// </summary>
    /// <param name="message">What is wrong, written for a person.</param>
    /// <param name="member">The part of the message the failure applies to, when it applies to one.</param>
    /// <param name="code">A machine-readable code for the failure, when the validator supplies one.</param>
    /// <returns>A validity carrying one failure.</returns>
    /// <exception cref="ArgumentException"><paramref name="message" /> is null, empty, or whitespace.</exception>
    public static Validity Invalid(string message, string? member = null, string? code = null)
    {
        return new Validity([new ValidationFailure(message, member, code)]);
    }

    /// <summary>
    ///     Reports the message malformed for the given failures.
    /// </summary>
    /// <param name="failures">The failures the validator found.</param>
    /// <returns>A validity carrying every failure, or <see cref="Valid" /> when the sequence is empty.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="failures" /> is null.</exception>
    public static Validity Invalid(IEnumerable<ValidationFailure> failures)
    {
        ArgumentNullException.ThrowIfNull(failures);

        var collected = failures as IReadOnlyList<ValidationFailure> ?? failures.ToList();

        return collected.Count == 0 ? Valid : new Validity(collected);
    }

    /// <summary>
    ///     Reports the message malformed for the given failures.
    /// </summary>
    /// <param name="failures">The failures the validator found.</param>
    /// <returns>A validity carrying every failure, or <see cref="Valid" /> when none were given.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="failures" /> is null.</exception>
    public static Validity Invalid(params ValidationFailure[] failures)
    {
        ArgumentNullException.ThrowIfNull(failures);

        return failures.Length == 0 ? Valid : new Validity(failures);
    }

    /// <summary>
    ///     Determines whether two validity values are equal.
    /// </summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><see langword="true" /> when both carry the same failures.</returns>
    public static bool operator ==(Validity left, Validity right)
    {
        return left.Equals(right);
    }

    /// <summary>
    ///     Determines whether two validity values differ.
    /// </summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><see langword="true" /> when they carry different failures.</returns>
    public static bool operator !=(Validity left, Validity right)
    {
        return !left.Equals(right);
    }

    /// <inheritdoc />
    public bool Equals(Validity other)
    {
        return Failures.SequenceEqual(other.Failures);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is Validity other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();

        foreach (var failure in Failures)
        {
            hash.Add(failure);
        }

        return hash.ToHashCode();
    }

    /// <summary>
    ///     Converts this validity to the pipeline decision the stage runner acts on.
    /// </summary>
    /// <returns>
    ///     A decision reporting <see cref="MediationOutcome.Invalid" /> when failures were reported, otherwise
    ///     <see cref="PipelineDecision.Continue" />.
    /// </returns>
    /// <remarks>
    ///     The stage runner does not act on this decision immediately. It collects the failures from every validator and
    ///     builds one decision from all of them, because a caller fixing a malformed message wants every failure at once.
    /// </remarks>
    internal PipelineDecision ToDecision()
    {
        return IsInvalid ? PipelineDecision.Invalid(Failures) : PipelineDecision.Continue;
    }
}
