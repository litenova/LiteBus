using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Sample.Commands;

/// <summary>
///     Refuses a payment large enough to need a second approver.
/// </summary>
/// <remarks>
///     <para>
///         A guard answers one question: may this happen. Refusing an oversized payment is a denial, so the mediation
///         reports <see cref="MessageOutcome.Denied" /> and an audit trail records it as one, which is the list a
///         security review reads.
///     </para>
///     <para>
///         Skipping a payment this process already applied is a different decision and lives in
///         <see cref="SkipAppliedPaymentShortcut" />. Splitting them is what lets LiteBus run every guard first: the
///         shortcut cannot acknowledge a replay of a payment this guard would have refused, whatever priorities either
///         class carries.
///     </para>
/// </remarks>
public sealed class RequireSecondApproverGuard : ICommandGuard<ProcessPaymentCommand>
{
    /// <summary>
    ///     The largest amount this sample accepts without a second approver.
    /// </summary>
    private const decimal ApprovalThreshold = 10_000m;

    /// <inheritdoc />
    public Task<Verdict> CheckAsync(ProcessPaymentCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        return Task.FromResult(message.Amount > ApprovalThreshold
            ? Verdict.Deny($"payments above {ApprovalThreshold:N0} need a second approver")
            : Verdict.Allow);
    }
}
