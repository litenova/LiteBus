using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Inbox.Abstractions;

namespace LiteBus.Inbox;

/// <summary>
///     Invokes registered inbox processor envelope hooks around dispatch.
/// </summary>
internal static class InboxProcessorHookRunner
{
    /// <summary>
    ///     Runs all hooks before dispatch begins.
    /// </summary>
    /// <param name="hooks">The registered hooks.</param>
    /// <param name="envelope">The leased inbox envelope.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>A task that completes before dispatch begins.</returns>
    internal static async Task RunBeforeDispatchAsync(
        IReadOnlyList<IInboxProcessorEnvelopeHook> hooks,
        InboxEnvelope envelope,
        CancellationToken cancellationToken)
    {
        foreach (var hook in hooks)
        {
            await hook.BeforeDispatchAsync(envelope, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Runs all hooks after dispatch completes successfully.
    /// </summary>
    /// <param name="hooks">The registered hooks.</param>
    /// <param name="envelope">The leased inbox envelope.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>A task that completes after hook post-processing finishes.</returns>
    internal static async Task RunAfterDispatchAsync(
        IReadOnlyList<IInboxProcessorEnvelopeHook> hooks,
        InboxEnvelope envelope,
        CancellationToken cancellationToken)
    {
        foreach (var hook in hooks)
        {
            await hook.AfterDispatchAsync(envelope, cancellationToken).ConfigureAwait(false);
        }
    }
}
