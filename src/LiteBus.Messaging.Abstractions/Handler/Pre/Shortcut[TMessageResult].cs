using System;
using System.Collections.Generic;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     The answer a shortcut over a message that produces a result returns, carrying a result the compiler has checked.
/// </summary>
/// <typeparam name="TMessageResult">The result type of the message the shortcut runs for.</typeparam>
/// <remarks>
///     <para>
///         A shortcut that answers has to supply the value the caller receives, because the main handler never runs.
///         Typing the answer over the result type is what turns supplying the wrong value from a runtime configuration
///         error into a compile error.
///     </para>
///     <para>
///         Answering is not refusing. The mediation reports <see cref="MessageOutcome.ShortCircuited" /> and an audit
///         trail records a success, because a cache hit refused nobody. A guard refuses.
///     </para>
/// </remarks>
/// <example>
///     <code><![CDATA[
/// public sealed class ServeProductFromCache : IQueryShortcut<GetProductQuery, ProductView>
/// {
///     public async Task<Shortcut<ProductView>> TryAnswerAsync(
///         GetProductQuery query,
///         CancellationToken cancellationToken = default)
///     {
///         var cached = await _cache.TryGetAsync(query.ProductId, cancellationToken);
///
///         return cached is null
///             ? Shortcut<ProductView>.None
///             : Shortcut<ProductView>.Answer(cached, "served from cache");
///     }
/// }
/// ]]></code>
/// </example>
public readonly struct Shortcut<TMessageResult> : IEquatable<Shortcut<TMessageResult>>
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="Shortcut{TMessageResult}" /> struct.
    /// </summary>
    /// <param name="isAnswered">Whether the shortcut answered for the main handler.</param>
    /// <param name="hasResult">Whether the shortcut supplied the result the caller receives.</param>
    /// <param name="result">The result returned to the caller.</param>
    /// <param name="reason">The reason the main handler was skipped.</param>
    private Shortcut(bool isAnswered, bool hasResult, TMessageResult? result, string? reason)
    {
        IsAnswered = isAnswered;
        HasResult = hasResult;
        Result = result;
        Reason = reason;
    }

    /// <summary>
    ///     Gets a shortcut that supplies no answer, so the mediation proceeds.
    /// </summary>
    public static Shortcut<TMessageResult> None => default;

    /// <summary>
    ///     Gets a value indicating whether the shortcut answered for the main handler.
    /// </summary>
    public bool IsAnswered { get; }

    /// <summary>
    ///     Gets a value indicating whether the shortcut supplied the result the caller receives.
    /// </summary>
    public bool HasResult { get; }

    /// <summary>
    ///     Gets the result the caller receives instead of the one the main handler would have produced.
    /// </summary>
    /// <remarks>
    ///     Meaningful only when <see cref="HasResult" /> is <see langword="true" />.
    /// </remarks>
    public TMessageResult? Result { get; }

    /// <summary>
    ///     Gets the reason the main handler was skipped.
    /// </summary>
    public string? Reason { get; }

    /// <summary>
    ///     Creates a shortcut that answers for the main handler.
    /// </summary>
    /// <param name="result">The result the caller receives instead of the one the main handler would have produced.</param>
    /// <param name="reason">The reason the main handler was skipped.</param>
    /// <returns>An answering shortcut.</returns>
    /// <remarks>
    ///     The mediation reports <see cref="MessageOutcome.ShortCircuited" />, and an audit trail records a success,
    ///     because nothing was refused.
    /// </remarks>
    public static Shortcut<TMessageResult> Answer(TMessageResult result, string? reason = null)
    {
        return new Shortcut<TMessageResult>(isAnswered: true, hasResult: true, result, reason);
    }

    /// <summary>
    ///     Creates a shortcut that skips the main handler without supplying a result.
    /// </summary>
    /// <param name="reason">The reason the main handler was skipped.</param>
    /// <returns>An answering shortcut that supplies no result.</returns>
    /// <remarks>
    ///     This is meaningful for a stream, where supplying no stream is a legitimate answer and means the caller
    ///     enumerates nothing. For any other message that produces a result, the mediation has nothing to hand back and
    ///     raises <see cref="Runtime.Abstractions.Exceptions.LiteBusConfigurationException" />; use
    ///     <see cref="Answer" /> there.
    /// </remarks>
    public static Shortcut<TMessageResult> Skip(string? reason = null)
    {
        return new Shortcut<TMessageResult>(isAnswered: true, hasResult: false, result: default, reason);
    }

    /// <summary>
    ///     Determines whether two shortcuts are equal.
    /// </summary>
    /// <param name="left">The first shortcut.</param>
    /// <param name="right">The second shortcut.</param>
    /// <returns><see langword="true" /> when the shortcuts are equal.</returns>
    public static bool operator ==(Shortcut<TMessageResult> left, Shortcut<TMessageResult> right)
    {
        return left.Equals(right);
    }

    /// <summary>
    ///     Determines whether two shortcuts differ.
    /// </summary>
    /// <param name="left">The first shortcut.</param>
    /// <param name="right">The second shortcut.</param>
    /// <returns><see langword="true" /> when the shortcuts differ.</returns>
    public static bool operator !=(Shortcut<TMessageResult> left, Shortcut<TMessageResult> right)
    {
        return !left.Equals(right);
    }

    /// <inheritdoc />
    public bool Equals(Shortcut<TMessageResult> other)
    {
        return IsAnswered == other.IsAnswered
               && HasResult == other.HasResult
               && EqualityComparer<TMessageResult?>.Default.Equals(Result, other.Result)
               && string.Equals(Reason, other.Reason, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is Shortcut<TMessageResult> other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(IsAnswered, HasResult, Result, Reason);
    }

    /// <summary>
    ///     Converts this shortcut to the stop the pipeline acts on.
    /// </summary>
    /// <returns>The stop for an answer, or <see cref="PipelineStop.None" /> when the mediation proceeds.</returns>
    internal PipelineStop ToStop()
    {
        return IsAnswered
            ? PipelineStop.ShortCircuited(Reason, HasResult, Result)
            : PipelineStop.None;
    }
}
