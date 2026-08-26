using System.Collections.Concurrent;
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Sample.Commands;

/// <summary>
///     Decides whether a payment command reaches its handler.
/// </summary>
/// <remarks>
///     <para>
///         A gate is the one stage that can stop the pipeline cleanly, and it separates the two reasons for stopping.
///         Refusing an oversized payment is a denial: the actor was not permitted, and an audit trail records it as one.
///         Skipping a payment this process already applied is a short-circuit: nothing was refused, so the trail records
///         a success.
///     </para>
///     <para>
///         The distinction matters because a security review reads denials. Recording a replayed payment as a refusal
///         would put an entry in that list that never happened.
///     </para>
/// </remarks>
public sealed class ProcessPaymentCommandGate : ICommandGate<ProcessPaymentCommand>
{
    /// <summary>
    ///     The largest amount this sample accepts without a second approver.
    /// </summary>
    private const decimal ApprovalThreshold = 10_000m;

    /// <summary>
    ///     The payments already applied in this process, standing in for an idempotency store.
    /// </summary>
    private static readonly ConcurrentDictionary<Guid, bool> Applied = new();

    /// <inheritdoc />
    public Task<PipelineDirective> DecideAsync(
        ProcessPaymentCommand message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (message.Amount > ApprovalThreshold)
        {
            return Task.FromResult(PipelineDirective.Deny(
                $"payments above {ApprovalThreshold:N0} need a second approver"));
        }

        if (!Applied.TryAdd(message.PaymentId, true))
        {
            return Task.FromResult(PipelineDirective.ShortCircuit("the payment was already applied"));
        }

        return Task.FromResult(PipelineDirective.Continue);
    }
}
