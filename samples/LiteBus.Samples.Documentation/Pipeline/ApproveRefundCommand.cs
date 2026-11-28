using LiteBus.Commands.Abstractions;

namespace LiteBus.Samples.Documentation.Pipeline;

/// <summary>
///     Requests approval of a customer refund.
/// </summary>
/// <param name="ApproverId">The identifier of the approver.</param>
/// <param name="RequesterId">The identifier of the requester.</param>
public sealed record ApproveRefundCommand(Guid ApproverId, Guid RequesterId) : ICommand;
