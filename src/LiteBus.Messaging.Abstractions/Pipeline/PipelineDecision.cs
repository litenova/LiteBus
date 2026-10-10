using System;
using System.Collections.Generic;
using System.Linq;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Carries a pre-stage decision from the handler that made it to the mediation strategy that acts on it.
/// </summary>
/// <remarks>
///     <para>
///         This is infrastructure. Handlers return <see cref="Verdict" />, <see cref="Validity" />, or
///         <see cref="Shortcut" />, and the stage runner converts whichever it received into one of these so every
///         strategy can act on a single shape.
///     </para>
///     <para>
///         Three decisions stop the pipeline and one lets it continue. Read <see cref="StopsPipeline" /> first, then
///         <see cref="Outcome" /> to tell the three stopping decisions apart:
///         <see cref="MediationOutcome.Denied" />, <see cref="MediationOutcome.Invalid" />, and
///         <see cref="MediationOutcome.Answered" />. No other outcome can reach this type.
///     </para>
///     <para>
///         Two kinds of stopping decision exist and they are not interchangeable. A refusal from a guard or a validator carries no
///         result, because a refusal does not owe the caller the value the main handler would have produced; the value a
///         refused caller receives comes from an <see cref="IMessageRefusalMapper{TMessage,TMessageResult}" /> when one
///         is registered, and otherwise the refusal reaches the caller as an exception. An answer from a shortcut
///         carries the result itself, because a shortcut is supplying the value the handler would have produced.
///     </para>
/// </remarks>
public readonly struct PipelineDecision : IEquatable<PipelineDecision>
{
    /// <summary>
    ///     The empty list returned for any decision other than a validation failure, shared to avoid allocating.
    /// </summary>
    private static readonly IReadOnlyList<ValidationFailure> NoFailures = [];

    /// <summary>
    ///     The failures the validator stage collected, or null for any other decision.
    /// </summary>
    private readonly IReadOnlyList<ValidationFailure>? _failures;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PipelineDecision" /> struct.
    /// </summary>
    /// <param name="stopsPipeline">Whether the decision stops the pipeline.</param>
    /// <param name="outcome">The outcome the mediation reports.</param>
    /// <param name="hasResult">Whether a shortcut supplied a result.</param>
    /// <param name="result">The result a shortcut supplied.</param>
    /// <param name="reason">The reason the decision supplied.</param>
    /// <param name="code">The code the decision supplied.</param>
    /// <param name="failures">The failures a validator collected.</param>
    /// <param name="answeredBy">The shortcut that answered, when one did.</param>
    private PipelineDecision(
        bool stopsPipeline,
        MediationOutcome outcome,
        bool hasResult,
        object? result,
        string? reason,
        string? code,
        IReadOnlyList<ValidationFailure>? failures,
        Type? answeredBy = null)
    {
        StopsPipeline = stopsPipeline;
        Outcome = outcome;
        HasResult = hasResult;
        Result = result;
        Reason = reason;
        Code = code;
        _failures = failures;
        AnsweredBy = answeredBy;
    }

    /// <summary>
    ///     Gets the decision that lets the pipeline continue.
    /// </summary>
    /// <value>The default value, so a handler that decides nothing allocates nothing.</value>
    public static PipelineDecision Continue => default;

    /// <summary>
    ///     Gets a value indicating whether the pipeline stops here.
    /// </summary>
    /// <value><see langword="true" /> when the main handler must not run.</value>
    public bool StopsPipeline { get; }

    /// <summary>
    ///     Gets the outcome the mediation reports for this decision.
    /// </summary>
    /// <value>
    ///     <see cref="MediationOutcome.Denied" /> for a guard refusal, <see cref="MediationOutcome.Invalid" /> for a
    ///     validation failure, or <see cref="MediationOutcome.Answered" /> for a shortcut.
    /// </value>
    public MediationOutcome Outcome { get; }

    /// <summary>
    ///     Gets a value indicating whether a shortcut supplied a result.
    /// </summary>
    /// <value><see langword="true" /> when <see cref="Result" /> holds the value the caller receives.</value>
    public bool HasResult { get; }

    /// <summary>
    ///     Gets the result a shortcut supplied.
    /// </summary>
    /// <value>The value the caller receives, or <see langword="null" /> when no shortcut supplied one.</value>
    public object? Result { get; }

    /// <summary>
    ///     Gets the reason the decision supplied.
    /// </summary>
    /// <value>The reason, or <see langword="null" /> when the decision supplied none.</value>
    public string? Reason { get; }

    /// <summary>
    ///     Gets the code the decision supplied.
    /// </summary>
    /// <value>The machine-readable code, or <see langword="null" /> when the decision supplied none.</value>
    /// <remarks>
    ///     Every decision shape carries one and it means the same thing in all of them: something a later stage can
    ///     switch on, where <see cref="Reason" /> is prose. A guard supplies it through <see cref="Verdict.Deny" />, a
    ///     shortcut through <see cref="Shortcut.Answer" />, and the validator stage from the single failure it
    ///     collected, since two failures have no one code.
    /// </remarks>
    public string? Code { get; }

    /// <summary>
    ///     Gets the failures a validator collected.
    /// </summary>
    /// <value>Every failure the validator stage reported, or an empty list for any other decision.</value>
    public IReadOnlyList<ValidationFailure> Failures => _failures ?? NoFailures;

    /// <summary>
    ///     Gets the shortcut that answered the message.
    /// </summary>
    /// <value>
    ///     The concrete shortcut type when <see cref="Outcome" /> is <see cref="MediationOutcome.Answered" />, otherwise
    ///     <see langword="null" />.
    /// </value>
    /// <remarks>
    ///     Recorded so that a misconfigured shortcut can be named rather than merely described. A message reached by
    ///     several globally registered shortcuts gives the reader no way to tell which one answered, and the message
    ///     type alone does not narrow it down.
    /// </remarks>
    public Type? AnsweredBy { get; }

    /// <summary>
    ///     Gets a value indicating whether this decision refused the message.
    /// </summary>
    /// <value>
    ///     <see langword="true" /> for a guard refusal or a validation failure, which are the decisions a refusal mapper
    ///     covers. A shortcut answer is not a refusal.
    /// </value>
    public bool IsRefusal => Outcome is MediationOutcome.Denied or MediationOutcome.Invalid;

    /// <summary>
    ///     Determines whether two decisions are equal.
    /// </summary>
    /// <param name="left">The first decision.</param>
    /// <param name="right">The second decision.</param>
    /// <returns><see langword="true" /> when both carry the same decision.</returns>
    public static bool operator ==(PipelineDecision left, PipelineDecision right)
    {
        return left.Equals(right);
    }

    /// <summary>
    ///     Determines whether two decisions differ.
    /// </summary>
    /// <param name="left">The first decision.</param>
    /// <param name="right">The second decision.</param>
    /// <returns><see langword="true" /> when they carry different decisions.</returns>
    public static bool operator !=(PipelineDecision left, PipelineDecision right)
    {
        return !left.Equals(right);
    }

    /// <summary>
    ///     Describes this refusal for an <see cref="IMessageRefusalMapper{TMessage,TMessageResult}" />.
    /// </summary>
    /// <returns>The outcome, reason, and code the decision supplied.</returns>
    /// <remarks>
    ///     A validation failure with several failures reports them joined into one reason, because a mapper receives one
    ///     refusal. A mapper that needs the failures individually reads them from
    ///     <see cref="LiteBusMessageInvalidException.Failures" /> on the exception path, or the application uses a
    ///     validator code to distinguish them.
    /// </remarks>
    /// <exception cref="System.InvalidOperationException">This decision is not a refusal.</exception>
    public Refusal ToRefusal()
    {
        var reason = Reason ?? "no reason was given";

        return Outcome switch
        {
            MediationOutcome.Denied => Refusal.Denied(reason, Code),
            MediationOutcome.Invalid => Refusal.Invalid(reason, Code),
            _ => throw new InvalidOperationException(
                $"A '{Outcome}' decision is not a refusal, so it has no refusal to describe. Test IsRefusal first.")
        };
    }

    /// <summary>
    ///     Creates the exception a refused caller receives when no refusal mapper is registered.
    /// </summary>
    /// <param name="messageType">The type of the message that was refused.</param>
    /// <returns>
    ///     <see cref="LiteBusMessageDeniedException" /> for a guard refusal, or
    ///     <see cref="LiteBusMessageInvalidException" /> for a validation failure.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="messageType" /> is null.</exception>
    public Exception CreateRefusalException(Type messageType)
    {
        ArgumentNullException.ThrowIfNull(messageType);

        return Outcome == MediationOutcome.Invalid
            ? new LiteBusMessageInvalidException(messageType, Failures)
            : new LiteBusMessageDeniedException(messageType, Reason ?? "no reason was given", Code);
    }

    /// <summary>
    ///     Resolves the result a shortcut supplied to the type the caller expects.
    /// </summary>
    /// <typeparam name="TMessageResult">The type of result the message produces.</typeparam>
    /// <param name="messageType">The type of the message being mediated.</param>
    /// <returns>The result the caller receives.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="messageType" /> is null.</exception>
    /// <exception cref="PipelineContractException">
    ///     A shortcut answered without supplying the result the caller expects, or supplied one of the wrong type.
    /// </exception>
    /// <remarks>
    ///     The missing-result case is reachable only from <see cref="IMessageShortcut{TMessage}" />, the untyped
    ///     shortcut, used on a message that produces a result. <see cref="Shortcut{TMessageResult}" /> always carries the
    ///     value, so the typed contract cannot reach this failure. Registering the untyped contract directly against
    ///     such a message is rejected when the registry links the two, and analyzer LB1019 reports it at compile time.
    ///     What reaches here is the case neither can prove: a shortcut registered against a base type or interface,
    ///     which is legitimate for the result-less messages beneath it and only wrong for this one.
    /// </remarks>
    public TMessageResult ResolveResult<TMessageResult>(Type messageType)
    {
        ArgumentNullException.ThrowIfNull(messageType);

        if (!HasResult)
        {
            var culprit = AnsweredBy?.Name ?? "A shortcut";

            throw new PipelineContractException(
                $"'{culprit}' answered the mediation of '{messageType.Name}' through the untyped shortcut contract, "
                + $"which cannot carry the '{typeof(TMessageResult).Name}' the caller expects. Implement "
                + $"IMessageShortcut<{messageType.Name}, {typeof(TMessageResult).Name}> so the compiler requires the "
                + "result, and pass it to Answer. A shortcut registered against a base type must return "
                + "Shortcut.None for the messages beneath it that produce a result.");
        }

        if (Result is null)
        {
            return default!;
        }

        if (Result is TMessageResult typedResult)
        {
            return typedResult;
        }

        throw new PipelineContractException(
            $"'{AnsweredBy?.Name ?? "A shortcut"}' answered '{messageType.Name}' with a result of type "
            + $"'{Result.GetType().Name}', but the message expects '{typeof(TMessageResult).Name}'.");
    }

    /// <inheritdoc />
    public bool Equals(PipelineDecision other)
    {
        return StopsPipeline == other.StopsPipeline
               && Outcome == other.Outcome
               && HasResult == other.HasResult
               && Equals(Result, other.Result)
               && string.Equals(Reason, other.Reason, StringComparison.Ordinal)
               && string.Equals(Code, other.Code, StringComparison.Ordinal)
               && AnsweredBy == other.AnsweredBy
               && Failures.SequenceEqual(other.Failures);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is PipelineDecision other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(StopsPipeline, Outcome, HasResult, Result, Reason, Code, AnsweredBy, Failures.Count);
    }

    /// <summary>
    ///     Creates the decision a refusing guard produces.
    /// </summary>
    /// <param name="reason">Why the message is refused.</param>
    /// <param name="code">The code the guard supplied, when any.</param>
    /// <returns>A decision reporting <see cref="MediationOutcome.Denied" />.</returns>
    internal static PipelineDecision Denied(string reason, string? code)
    {
        return new PipelineDecision(
            stopsPipeline: true,
            MediationOutcome.Denied,
            hasResult: false,
            result: null,
            reason,
            code,
            failures: null);
    }

    /// <summary>
    ///     Creates the decision the validator stage produces when it collected failures.
    /// </summary>
    /// <param name="failures">Every failure the stage collected.</param>
    /// <returns>A decision reporting <see cref="MediationOutcome.Invalid" />.</returns>
    internal static PipelineDecision Invalid(IReadOnlyList<ValidationFailure> failures)
    {
        var reason = string.Join("; ", failures.Select(failure => failure.ToString()));
        var code = failures.Count == 1 ? failures[0].Code : null;

        return new PipelineDecision(
            stopsPipeline: true,
            MediationOutcome.Invalid,
            hasResult: false,
            result: null,
            reason,
            code,
            failures);
    }

    /// <summary>
    ///     Creates the decision an answering shortcut produces.
    /// </summary>
    /// <param name="reason">Why the shortcut answered, when it said.</param>
    /// <param name="code">The machine-readable code the shortcut supplied, when it supplied one.</param>
    /// <param name="hasResult">Whether the shortcut supplied a result.</param>
    /// <param name="result">The result the shortcut supplied.</param>
    /// <param name="answeredBy">The shortcut that answered.</param>
    /// <returns>A decision reporting <see cref="MediationOutcome.Answered" />.</returns>
    internal static PipelineDecision Answered(
        string? reason,
        string? code,
        bool hasResult,
        object? result,
        Type? answeredBy = null)
    {
        return new PipelineDecision(
            stopsPipeline: true,
            MediationOutcome.Answered,
            hasResult,
            result,
            reason,
            code,
            failures: null,
            answeredBy);
    }
}
