using System;

namespace LiteBus.Messaging.Abstractions.Authorization;

/// <summary>
///     The outcome of one authorization policy evaluation: the message may proceed, it is denied, or evaluation
///     failed with a fault.
/// </summary>
/// <remarks>
///     <para>
///         A denial is a caller-facing refusal with a stable code and a human reason. A fault is a configuration or
///         dependency failure an operator must fix, such as a missing permission lookup or an unknown sign-in
///         instant. The two are kept apart because a fault is never a refusal: combinators and refusal rewriting
///         pass it through unchanged, and the policy layer maps it to a pipeline fault rather than a denial.
///     </para>
///     <para>
///         Guards keep returning the two-state <see cref="Verdict" /> at the pipeline boundary; the policy guard maps
///         a denial to a denial verdict and a fault to a pipeline fault.
///     </para>
/// </remarks>
public abstract record PolicyResult
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="PolicyResult" /> class.
    /// </summary>
    /// <remarks>
    ///     Internal so an evaluation can only end as <see cref="PolicyAllow" />, <see cref="PolicyDeny" />, or
    ///     <see cref="PolicyFault" />.
    /// </remarks>
    internal PolicyResult()
    {
    }

    /// <summary>
    ///     Gets the result that permits the message to proceed.
    /// </summary>
    public static PolicyAllow Allow { get; } = new PolicyAllow();

    /// <summary>
    ///     Creates the result that denies the message with a caller-facing refusal.
    /// </summary>
    /// <param name="code">The stable refusal code carried on the denial.</param>
    /// <param name="reason">Why the message is denied, written for a person.</param>
    /// <returns>The denial result.</returns>
    /// <exception cref="ArgumentException"><paramref name="code" /> or <paramref name="reason" /> is null, empty, or whitespace.</exception>
    public static PolicyDeny Deny(string code, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        return new PolicyDeny(code, reason);
    }

    /// <summary>
    ///     Creates the result that reports evaluation failed with a configuration or dependency fault.
    /// </summary>
    /// <param name="code">The stable fault code operators switch on.</param>
    /// <param name="reason">What failed, written for a person.</param>
    /// <param name="cause">The fault that was caught, or <see langword="null" /> when none was.</param>
    /// <returns>The fault result.</returns>
    /// <exception cref="ArgumentException"><paramref name="code" /> or <paramref name="reason" /> is null, empty, or whitespace.</exception>
    public static PolicyFault Fault(string code, string reason, Exception? cause = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        return new PolicyFault(code, reason, cause);
    }
}
