using System.Collections.Concurrent;
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Sample.Commands;

/// <summary>
///     Skips a payment this process has already applied.
/// </summary>
/// <remarks>
///     <para>
///         A shortcut answers one question: is this already done. Nothing was refused, so the mediation reports
///         <see cref="MediationOutcome.Answered" /> and an audit trail records a success. Recording a replayed
///         payment as a refusal would put an entry in the denial list that never happened.
///     </para>
///     <para>
///         This runs only after <see cref="RequireSecondApproverGuard" /> has allowed the payment, because the framework
///         fixes that order. It also runs before the pre-handler stage, so a replay does not pay for validation and
///         enrichment it is about to skip.
///     </para>
/// </remarks>
public sealed class SkipAppliedPaymentShortcut : ICommandShortcut<ProcessPaymentCommand>
{
    /// <summary>
    ///     The payments already applied in this process, standing in for an idempotency store.
    /// </summary>
    private static readonly ConcurrentDictionary<Guid, bool> Applied = new();

    /// <inheritdoc />
    public Task<Shortcut> TryAnswerAsync(ProcessPaymentCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        return Task.FromResult(Applied.TryAdd(message.PaymentId, true)
            ? Shortcut.None
            : Shortcut.Answer("the payment was already applied"));
    }
}
