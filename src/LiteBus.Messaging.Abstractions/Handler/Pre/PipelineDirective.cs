using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     The decision a gate returns: continue the pipeline, stop because the result is already known, or stop because the
///     message is refused.
/// </summary>
/// <remarks>
///     <para>
///         This is the shape returned by <see cref="IMessageGate{TMessage}" />, for messages that produce no result. A
///         gate over a message that produces a result returns <see cref="PipelineDirective{TMessageResult}" /> instead,
///         so the compiler checks the value it supplies.
///     </para>
///     <para>
///         The decision is a return value rather than an exception. That makes it visible in the gate signature, lets
///         the compiler require it, and keeps an expected control-flow path off the exception path.
///     </para>
///     <para>
///         Only a gate can stop the pipeline, because stopping means skipping the work. Once the main handler has run
///         there is nothing left to skip; a handler that wants to suppress the reactions to a no-op calls
///         <see cref="IExecutionContext.SuppressPostHandlers" /> instead.
///     </para>
/// </remarks>
/// <example>
///     <code><![CDATA[
/// public sealed class RejectClosedAccount : ICommandGate<WithdrawCommand>
/// {
///     public async Task<PipelineDirective> PreHandleAsync(
///         WithdrawCommand command,
///         CancellationToken cancellationToken = default)
///     {
///         var account = await _accounts.GetAsync(command.AccountId, cancellationToken);
///
///         if (account.IsClosed)
///         {
///             return PipelineDirective.Deny("the account is closed");
///         }
///
///         return account.AlreadyApplied(command.TransferId)
///             ? PipelineDirective.ShortCircuit("the transfer was already applied")
///             : PipelineDirective.Continue;
///     }
/// }
/// ]]></code>
/// </example>
public readonly struct PipelineDirective : IEquatable<PipelineDirective>
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="PipelineDirective" /> struct.
    /// </summary>
    /// <param name="kind">What the directive tells the pipeline to do.</param>
    /// <param name="hasResult">Whether the directive supplies the result the caller receives.</param>
    /// <param name="result">The result returned to the caller when the pipeline stops.</param>
    /// <param name="reason">The reason the pipeline stopped.</param>
    private PipelineDirective(PipelineDirectiveKind kind, bool hasResult, object? result, string? reason)
    {
        Kind = kind;
        HasResult = hasResult;
        Result = result;
        Reason = reason;
    }

    /// <summary>
    ///     Gets a directive that lets the pipeline proceed.
    /// </summary>
    public static PipelineDirective Continue => default;

    /// <summary>
    ///     Gets what the directive tells the pipeline to do.
    /// </summary>
    public PipelineDirectiveKind Kind { get; }

    /// <summary>
    ///     Gets a value indicating whether the directive supplies the result the caller receives.
    /// </summary>
    /// <remarks>
    ///     This is distinct from <see cref="Result" /> being <see langword="null" />, because a message whose result
    ///     type is nullable may legitimately stop with a null result.
    /// </remarks>
    public bool HasResult { get; }

    /// <summary>
    ///     Gets the result returned to the caller when the pipeline stops.
    /// </summary>
    /// <remarks>
    ///     Meaningful only when <see cref="HasResult" /> is <see langword="true" />. A typed directive is the only way to
    ///     supply one, so the compiler can check that it matches the result type of the message.
    /// </remarks>
    public object? Result { get; }

    /// <summary>
    ///     Gets the reason the pipeline stopped.
    /// </summary>
    /// <remarks>
    ///     A stopped mediation reaches neither post-handlers nor error handlers, so this reason is the only description
    ///     of why the message ended. It reaches completion handlers as <see cref="MessageCompletionContext.Reason" />
    ///     and an audit trail as the reason on the record. It is always present on a denial.
    /// </remarks>
    public string? Reason { get; }

    /// <summary>
    ///     Gets a value indicating whether the directive stops the pipeline before the main handler runs.
    /// </summary>
    public bool StopsPipeline => Kind is not PipelineDirectiveKind.Continue;

    /// <summary>
    ///     Creates a directive that stops the pipeline because the result is already known.
    /// </summary>
    /// <param name="reason">The reason the main handler was skipped.</param>
    /// <returns>A short-circuiting directive that supplies no result.</returns>
    /// <remarks>
    ///     Use this from a gate over a message that produces no result. The mediation reports
    ///     <see cref="MessageOutcome.ShortCircuited" />, and an audit trail records a success, because nothing was
    ///     refused.
    /// </remarks>
    public static PipelineDirective ShortCircuit(string? reason = null)
    {
        return new PipelineDirective(PipelineDirectiveKind.ShortCircuit, hasResult: false, result: null, reason);
    }

    /// <summary>
    ///     Creates a directive that stops the pipeline because the message is refused.
    /// </summary>
    /// <param name="reason">The reason the message was refused.</param>
    /// <returns>A denying directive that supplies no result.</returns>
    /// <remarks>
    ///     The mediation reports <see cref="MessageOutcome.Denied" /> and then throws
    ///     <see cref="LiteBusMessageDeniedException" />, because a refusal with no result has nothing to hand back to
    ///     the caller. Supply a result through the typed overload to have the caller receive a refusal value instead.
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown when <paramref name="reason" /> is null or whitespace.</exception>
    public static PipelineDirective Deny(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new PipelineDirective(PipelineDirectiveKind.Deny, hasResult: false, result: null, reason);
    }

    /// <summary>
    ///     Determines whether two directives are equal.
    /// </summary>
    /// <param name="left">The first directive.</param>
    /// <param name="right">The second directive.</param>
    /// <returns><see langword="true" /> when the directives are equal.</returns>
    public static bool operator ==(PipelineDirective left, PipelineDirective right)
    {
        return left.Equals(right);
    }

    /// <summary>
    ///     Determines whether two directives differ.
    /// </summary>
    /// <param name="left">The first directive.</param>
    /// <param name="right">The second directive.</param>
    /// <returns><see langword="true" /> when the directives differ.</returns>
    public static bool operator !=(PipelineDirective left, PipelineDirective right)
    {
        return !left.Equals(right);
    }

    /// <summary>
    ///     Creates a directive that stops the pipeline and carries the result supplied by a typed directive.
    /// </summary>
    /// <param name="kind">What the directive tells the pipeline to do.</param>
    /// <param name="hasResult">Whether the directive supplies the result the caller receives.</param>
    /// <param name="result">The result returned to the caller.</param>
    /// <param name="reason">The reason the pipeline stopped.</param>
    /// <returns>The untyped directive the pipeline acts on.</returns>
    /// <remarks>
    ///     A typed directive is the only public way to supply a result, so this stays internal to the assembly.
    /// </remarks>
    internal static PipelineDirective Stop(PipelineDirectiveKind kind, bool hasResult, object? result, string? reason)
    {
        return new PipelineDirective(kind, hasResult, result, reason);
    }

    /// <inheritdoc />
    public bool Equals(PipelineDirective other)
    {
        return Kind == other.Kind
               && HasResult == other.HasResult
               && Equals(Result, other.Result)
               && string.Equals(Reason, other.Reason, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is PipelineDirective other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(Kind, HasResult, Result, Reason);
    }
}
