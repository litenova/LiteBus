// <docs-snippet pipeline-command-guard>
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Samples.Documentation.Pipeline;

/// <summary>
///     Refuses a refund that the requester approved themselves.
/// </summary>
public sealed class RejectSelfApproval : ICommandGuard<ApproveRefundCommand>
{
    /// <inheritdoc />
    public Task<Verdict> DecideAsync(ApproveRefundCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        return Task.FromResult(command.ApproverId == command.RequesterId
            ? Verdict.Deny("the approver is the requester", code: "SELF_APPROVAL")
            : Verdict.Allow);
    }
}
// </docs-snippet>
