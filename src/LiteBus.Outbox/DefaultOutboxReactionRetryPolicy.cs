using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Outbox.Abstractions;

namespace LiteBus.Outbox;

/// <summary>
///     Retries a conflicted reaction with an exponential delay from <see cref="OutboxReactionRetryOptions" />.
/// </summary>
/// <remarks>
///     <para>
///         The dispatcher consults a retry policy only for a classified storage conflict, so this default never
///         decides over a business refusal or a configuration fault. It gives up once
///         <see cref="OutboxReactionRetryContext.ReactionAttempt" /> reaches
///         <see cref="OutboxReactionRetryOptions.MaxAttempts" />; the dispatcher enforces the same bound for every
///         policy, so a replacement cannot hold a lease forever either.
///     </para>
///     <para>
///         The delay doubles per failed attempt from <see cref="OutboxReactionRetryOptions.InitialDelay" />, capped
///         at <see cref="OutboxReactionRetryOptions.MaxDelay" />, with a ±20% jitter factor when
///         <see cref="OutboxReactionRetryOptions.UseJitter" /> is set.
///     </para>
/// </remarks>
public sealed class DefaultOutboxReactionRetryPolicy : IOutboxReactionRetryPolicy
{
    /// <summary>
    ///     Gets the bound and delay curve this policy decides from.
    /// </summary>
    private readonly OutboxReactionRetryOptions _options;

    /// <summary>
    ///     Initializes a new instance of the <see cref="DefaultOutboxReactionRetryPolicy" /> class.
    /// </summary>
    /// <param name="options">The bound and delay curve, or <see langword="null" /> for the defaults.</param>
    public DefaultOutboxReactionRetryPolicy(OutboxReactionRetryOptions? options = null)
    {
        _options = options ?? new OutboxReactionRetryOptions();
    }

    /// <inheritdoc />
    public ValueTask<OutboxReactionRetryDecision> DecideAsync(
        OutboxReactionRetryContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        OutboxReactionRetryDecision decision = context.ReactionAttempt >= _options.MaxAttempts
            ? new OutboxReactionRetryDecision.GiveUp()
            : new OutboxReactionRetryDecision.Retry(CalculateDelay(context.ReactionAttempt));

        return new ValueTask<OutboxReactionRetryDecision>(decision);
    }

    /// <summary>
    ///     Calculates the delay before the next in-dispatch attempt.
    /// </summary>
    /// <param name="reactionAttempt">The 1-based in-dispatch attempt that just failed.</param>
    /// <returns>The delay to wait before publishing again.</returns>
    private TimeSpan CalculateDelay(int reactionAttempt)
    {
        var rawTicks = _options.InitialDelay.Ticks * Math.Pow(2, Math.Max(0, reactionAttempt - 1));
        var delay = TimeSpan.FromTicks((long) Math.Min(rawTicks, _options.MaxDelay.Ticks));

        if (!_options.UseJitter || delay == TimeSpan.Zero)
        {
            return delay;
        }

        var jitterFactor = 0.8 + Random.Shared.NextDouble() * 0.4;
        return TimeSpan.FromTicks((long) Math.Min(delay.Ticks * jitterFactor, _options.MaxDelay.Ticks));
    }
}
