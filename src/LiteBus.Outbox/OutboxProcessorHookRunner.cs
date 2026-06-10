using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Orchestration.Abstractions;
using LiteBus.Outbox.Abstractions;

namespace LiteBus.Outbox;

/// <summary>
///     Invokes orchestration processor envelope hooks around outbox dispatch.
/// </summary>
internal static class OutboxProcessorHookRunner
{
    /// <summary>
    ///     Runs all hooks before dispatch begins.
    /// </summary>
    /// <param name="hooks">The registered orchestration hooks.</param>
    /// <param name="envelope">The leased outbox envelope.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>A task that completes before dispatch begins.</returns>
    internal static async Task RunBeforeDispatchAsync(
        IReadOnlyList<IProcessorEnvelopeHook> hooks,
        OutboxEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var context = new OutboxProcessorEnvelopeAdapter(envelope);

        foreach (var hook in hooks)
        {
            await hook.BeforeDispatchAsync(context, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Runs all hooks after dispatch completes successfully and terminal state is persisted.
    /// </summary>
    /// <param name="hooks">The registered orchestration hooks.</param>
    /// <param name="envelope">The leased outbox envelope.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>A task that completes after hook post-processing finishes.</returns>
    internal static async Task RunAfterDispatchAsync(
        IReadOnlyList<IProcessorEnvelopeHook> hooks,
        OutboxEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var context = new OutboxProcessorEnvelopeAdapter(envelope);

        foreach (var hook in hooks)
        {
            await hook.AfterDispatchAsync(context, cancellationToken).ConfigureAwait(false);
        }
    }
}
