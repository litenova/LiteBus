using System;
using System.Collections.Generic;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     The decision a gate over a message that produces a result returns, carrying a result the compiler has checked.
/// </summary>
/// <typeparam name="TMessageResult">The result type of the message the gate runs for.</typeparam>
/// <remarks>
///     <para>
///         A gate that stops the pipeline has to supply the value the caller receives, because the main handler never
///         runs. Typing the directive over the result type is what turns supplying the wrong value from a runtime
///         configuration error into a compile error.
///     </para>
///     <para>
///         Denial is the one case that may stop without a result. A refusal often has nothing meaningful to hand back,
///         so <see cref="Deny(string)" /> ends the mediation with <see cref="LiteBusMessageDeniedException" />, while
///         <see cref="Deny(string,TMessageResult)" /> hands the caller a refusal value such as a failed result object.
///     </para>
/// </remarks>
/// <example>
///     <code><![CDATA[
/// public sealed class ServeProductFromCache : IQueryGate<GetProductQuery, ProductView>
/// {
///     public async Task<PipelineDirective<ProductView>> DecideAsync(
///         GetProductQuery query,
///         CancellationToken cancellationToken = default)
///     {
///         var cached = await _cache.TryGetAsync(query.ProductId, cancellationToken);
///
///         return cached is null
///             ? PipelineDirective<ProductView>.Continue
///             : PipelineDirective<ProductView>.ShortCircuit(cached, "served from cache");
///     }
/// }
/// ]]></code>
/// </example>
public readonly struct PipelineDirective<TMessageResult> : IEquatable<PipelineDirective<TMessageResult>>
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="PipelineDirective{TMessageResult}" /> struct.
    /// </summary>
    /// <param name="kind">What the directive tells the pipeline to do.</param>
    /// <param name="hasResult">Whether the directive supplies the result the caller receives.</param>
    /// <param name="result">The result returned to the caller when the pipeline stops.</param>
    /// <param name="reason">The reason the pipeline stopped.</param>
    private PipelineDirective(PipelineDirectiveKind kind, bool hasResult, TMessageResult? result, string? reason)
    {
        Kind = kind;
        HasResult = hasResult;
        Result = result;
        Reason = reason;
    }

    /// <summary>
    ///     Gets a directive that lets the pipeline proceed.
    /// </summary>
    public static PipelineDirective<TMessageResult> Continue => default;

    /// <summary>
    ///     Gets what the directive tells the pipeline to do.
    /// </summary>
    public PipelineDirectiveKind Kind { get; }

    /// <summary>
    ///     Gets a value indicating whether the directive supplies the result the caller receives.
    /// </summary>
    public bool HasResult { get; }

    /// <summary>
    ///     Gets the result returned to the caller when the pipeline stops.
    /// </summary>
    /// <remarks>
    ///     Meaningful only when <see cref="HasResult" /> is <see langword="true" />.
    /// </remarks>
    public TMessageResult? Result { get; }

    /// <summary>
    ///     Gets the reason the pipeline stopped.
    /// </summary>
    public string? Reason { get; }

    /// <summary>
    ///     Gets a value indicating whether the directive stops the pipeline before the main handler runs.
    /// </summary>
    public bool StopsPipeline => Kind is not PipelineDirectiveKind.Continue;

    /// <summary>
    ///     Creates a directive that stops the pipeline because the result is already known.
    /// </summary>
    /// <param name="result">The result the caller receives instead of the one the main handler would have produced.</param>
    /// <param name="reason">The reason the main handler was skipped.</param>
    /// <returns>A short-circuiting directive.</returns>
    /// <remarks>
    ///     The mediation reports <see cref="MessageOutcome.ShortCircuited" />, and an audit trail records a success,
    ///     because nothing was refused.
    /// </remarks>
    public static PipelineDirective<TMessageResult> ShortCircuit(TMessageResult result, string? reason = null)
    {
        return new PipelineDirective<TMessageResult>(PipelineDirectiveKind.ShortCircuit, hasResult: true, result, reason);
    }

    /// <summary>
    ///     Creates a directive that refuses the message and hands the caller a refusal value.
    /// </summary>
    /// <param name="reason">The reason the message was refused.</param>
    /// <param name="result">The value the caller receives, such as a failed result object.</param>
    /// <returns>A denying directive that supplies a result.</returns>
    /// <remarks>
    ///     The mediation reports <see cref="MessageOutcome.Denied" />, which an audit trail records as a denial, and
    ///     returns <paramref name="result" /> to the caller without raising an exception.
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown when <paramref name="reason" /> is null or whitespace.</exception>
    public static PipelineDirective<TMessageResult> Deny(string reason, TMessageResult result)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new PipelineDirective<TMessageResult>(PipelineDirectiveKind.Deny, hasResult: true, result, reason);
    }

    /// <summary>
    ///     Creates a directive that refuses the message without supplying a result.
    /// </summary>
    /// <param name="reason">The reason the message was refused.</param>
    /// <returns>A denying directive that supplies no result.</returns>
    /// <remarks>
    ///     The mediation reports <see cref="MessageOutcome.Denied" /> and then throws
    ///     <see cref="LiteBusMessageDeniedException" />, because a refusal with no result has nothing to hand back to a
    ///     caller that expects a value.
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown when <paramref name="reason" /> is null or whitespace.</exception>
    public static PipelineDirective<TMessageResult> Deny(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new PipelineDirective<TMessageResult>(PipelineDirectiveKind.Deny, hasResult: false, result: default, reason);
    }

    /// <summary>
    ///     Converts a typed directive to the untyped directive the pipeline acts on.
    /// </summary>
    /// <param name="directive">The typed directive to convert.</param>
    /// <returns>The equivalent untyped directive.</returns>
    public static implicit operator PipelineDirective(PipelineDirective<TMessageResult> directive)
    {
        return directive.AsUntyped();
    }

    /// <summary>
    ///     Determines whether two directives are equal.
    /// </summary>
    /// <param name="left">The first directive.</param>
    /// <param name="right">The second directive.</param>
    /// <returns><see langword="true" /> when the directives are equal.</returns>
    public static bool operator ==(PipelineDirective<TMessageResult> left, PipelineDirective<TMessageResult> right)
    {
        return left.Equals(right);
    }

    /// <summary>
    ///     Determines whether two directives differ.
    /// </summary>
    /// <param name="left">The first directive.</param>
    /// <param name="right">The second directive.</param>
    /// <returns><see langword="true" /> when the directives differ.</returns>
    public static bool operator !=(PipelineDirective<TMessageResult> left, PipelineDirective<TMessageResult> right)
    {
        return !left.Equals(right);
    }

    /// <summary>
    ///     Converts this directive to the untyped directive the pipeline acts on.
    /// </summary>
    /// <returns>The equivalent untyped directive.</returns>
    public PipelineDirective AsUntyped()
    {
        return PipelineDirective.Stop(Kind, HasResult, Result, Reason);
    }

    /// <inheritdoc />
    public bool Equals(PipelineDirective<TMessageResult> other)
    {
        return Kind == other.Kind
               && HasResult == other.HasResult
               && EqualityComparer<TMessageResult?>.Default.Equals(Result, other.Result)
               && string.Equals(Reason, other.Reason, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is PipelineDirective<TMessageResult> other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(Kind, HasResult, Result, Reason);
    }
}
