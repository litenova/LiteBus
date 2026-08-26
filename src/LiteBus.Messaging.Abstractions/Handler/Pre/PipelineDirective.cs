using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     The decision a short-circuiting pre-handler returns: continue the pipeline, or stop before the work happens.
/// </summary>
/// <remarks>
///     <para>
///         Short-circuiting is a return value rather than an exception. That makes the decision visible in the
///         pre-handler's signature, lets the compiler require it to be returned, and keeps an expected control-flow path
///         off the exception path.
///     </para>
///     <para>
///         Only a pre-handler can short-circuit, because short-circuiting means skipping the work. Once the main handler
///         has run there is nothing left to skip; a handler that wants to suppress the reactions to a no-op calls
///         <see cref="IExecutionContext.SuppressPostHandlers" /> instead.
///     </para>
/// </remarks>
/// <example>
///     <code><![CDATA[
/// public sealed class ReturnCachedProduct : IQueryShortCircuitingPreHandler<GetProductQuery>
/// {
///     public async Task<PipelineDirective> PreHandleAsync(
///         GetProductQuery query,
///         CancellationToken cancellationToken = default)
///     {
///         var cached = await _cache.TryGetAsync(query.ProductId, cancellationToken);
///
///         return cached is null
///             ? PipelineDirective.Continue
///             : PipelineDirective.ShortCircuit(cached, "served from cache");
///     }
/// }
/// ]]></code>
/// </example>
public readonly struct PipelineDirective : IEquatable<PipelineDirective>
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="PipelineDirective" /> struct.
    /// </summary>
    /// <param name="isShortCircuit">Whether the pipeline stops before the main handler runs.</param>
    /// <param name="result">The result returned to the caller when the pipeline stops.</param>
    /// <param name="reason">The reason the pipeline stopped.</param>
    private PipelineDirective(bool isShortCircuit, object? result, string? reason)
    {
        IsShortCircuit = isShortCircuit;
        Result = result;
        Reason = reason;
    }

    /// <summary>
    ///     Gets a directive that lets the pipeline proceed.
    /// </summary>
    public static PipelineDirective Continue => default;

    /// <summary>
    ///     Gets a value indicating whether the pipeline stops before the main handler runs.
    /// </summary>
    public bool IsShortCircuit { get; }

    /// <summary>
    ///     Gets the result returned to the caller when the pipeline stops.
    /// </summary>
    /// <remarks>
    ///     Required when the mediated message has a result type. The value is cast by the mediation strategy, which
    ///     reports a configuration error when the type does not match.
    /// </remarks>
    public object? Result { get; }

    /// <summary>
    ///     Gets the reason the pipeline stopped.
    /// </summary>
    /// <remarks>
    ///     A short-circuited mediation reaches neither post-handlers nor error handlers, so this reason is the only
    ///     description of why the message ended. It reaches completion handlers as
    ///     <see cref="MessageCompletionContext.AbortReason" /> and an audit trail as the reason on the record.
    /// </remarks>
    public string? Reason { get; }

    /// <summary>
    ///     Creates a directive that stops the pipeline before the main handler runs.
    /// </summary>
    /// <param name="result">The result returned to the caller. Required when the message has a result type.</param>
    /// <param name="reason">The reason the pipeline stopped.</param>
    /// <returns>A short-circuiting directive.</returns>
    public static PipelineDirective ShortCircuit(object? result = null, string? reason = null)
    {
        return new PipelineDirective(isShortCircuit: true, result, reason);
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

    /// <inheritdoc />
    public bool Equals(PipelineDirective other)
    {
        return IsShortCircuit == other.IsShortCircuit
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
        return HashCode.Combine(IsShortCircuit, Result, Reason);
    }
}
