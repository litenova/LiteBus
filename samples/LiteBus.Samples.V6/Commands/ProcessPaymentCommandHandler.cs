using LiteBus.Commands.Abstractions;
using LiteBus.Inbox.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.DurableMessaging;
using LiteBus.Outbox.Abstractions;
using LiteBus.Samples.V6.Events;

namespace LiteBus.Samples.V6.Commands;

/// <summary>
///     Processes a payment command and emits a domain event through the outbox.
/// </summary>
public sealed class ProcessPaymentCommandHandler : ICommandHandler<ProcessPaymentCommand>
{
    private readonly IOutbox _outbox;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ProcessPaymentCommandHandler" /> class.
    /// </summary>
    /// <param name="outbox">The outbox writer used to store integration events.</param>
    public ProcessPaymentCommandHandler(IOutbox outbox)
    {
        _outbox = outbox;
    }

    /// <inheritdoc />
    public async Task HandleAsync(ProcessPaymentCommand command, CancellationToken cancellationToken = default)
    {
        if (AmbientExecutionContext.Current.Items.ContainsKey(InboxExecutionContextKeys.IsInboxExecution))
        {
            // Example: skip duplicate external side effects during inbox replay.
        }

        await _outbox.EnqueueAsync(
            OutboxEnqueueItem<PaymentProcessed>.From(
                new PaymentProcessed(command.PaymentId, command.Amount),
                OutboxEnqueueMetadata.Immediate with
                {
                    Target = new PublicationTarget.Topic("payments.payment-processed"),
                    Trace = new MessageTrace.Correlated(command.PaymentId.ToString())
                }),
            cancellationToken).ConfigureAwait(false);
    }
}