using LiteBus.Inbox.Abstractions;
using LiteBus.Samples.V6.Commands;
using Microsoft.AspNetCore.Mvc;

namespace LiteBus.Samples.V6.Controllers;

/// <summary>
///     Accepts payments into the inbox for background processing.
/// </summary>
[ApiController]
[Route("api/payments")]
public sealed class PaymentsController : ControllerBase
{
    private readonly IInbox _inbox;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PaymentsController" /> class.
    /// </summary>
    /// <param name="inbox">The inbox writer.</param>
    public PaymentsController(IInbox inbox)
    {
        _inbox = inbox;
    }

    /// <summary>
    ///     Accepts a payment command into the inbox.
    /// </summary>
    /// <param name="request">The payment request body.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>An acceptance response with the inbox receipt id.</returns>
    [HttpPost]
    public async Task<IActionResult> AcceptPaymentAsync(
        [FromBody] AcceptPaymentRequest request,
        CancellationToken cancellationToken)
    {
        var receipt = await _inbox.AddAsync(
            new ProcessPaymentCommand(request.PaymentId, request.Amount),
            new InboxOptions
            {
                IdempotencyKey = $"payment:{request.PaymentId}",
                CorrelationId = request.PaymentId.ToString()
            },
            cancellationToken).ConfigureAwait(false);

        return Accepted(new { receipt.Id, receipt.ContractName, receipt.AcceptedAt });
    }
}

/// <summary>
///     Request body for accepting a payment into the inbox.
/// </summary>
/// <param name="PaymentId">The payment identifier.</param>
/// <param name="Amount">The payment amount.</param>
public sealed record AcceptPaymentRequest(Guid PaymentId, decimal Amount);
