using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Inbox.Abstractions;
using LiteBus.Orchestration.Abstractions;

namespace LiteBus.Inbox;

/// <summary>
///     Invokes orchestration processor envelope hooks around inbox dispatch.
/// </summary>
internal static class InboxProcessorHookRunner
{
    /// <summary>
    ///     Runs all hooks before dispatch begins.
    /// </summary>
    /// <param name="hooks">The registered orchestration hooks.</param>
    /// <param name="envelope">The leased inbox envelope.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>A task that completes before dispatch begins.</returns>
    internal static async Task RunBeforeDispatchAsync(
        IReadOnlyList<IProcessorEnvelopeHook> hooks,
        InboxEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var context = new InboxProcessorEnvelopeAdapter(envelope);

        foreach (var hook in hooks)
        {
            await hook.BeforeDispatchAsync(context, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Re-establishes hook-owned dispatch scope before the axis dispatcher runs.
    /// </summary>
    /// <param name="hooks">The registered orchestration hooks.</param>
    /// <param name="envelope">The leased inbox envelope.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>A task that completes before dispatch begins.</returns>
    internal static async Task RunPrepareDispatchScopeAsync(
        IReadOnlyList<IProcessorEnvelopeHook> hooks,
        InboxEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var context = new InboxProcessorEnvelopeAdapter(envelope);

        foreach (var hook in hooks)
        {
            await hook.PrepareDispatchScopeAsync(context, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Runs all hooks after dispatch completes successfully and terminal state is persisted.
    /// </summary>
    /// <param name="hooks">The registered orchestration hooks.</param>
    /// <param name="envelope">The leased inbox envelope.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>A task that completes after hook post-processing finishes.</returns>
    internal static async Task RunAfterDispatchAsync(
        IReadOnlyList<IProcessorEnvelopeHook> hooks,
        InboxEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var context = new InboxProcessorEnvelopeAdapter(envelope);

        foreach (var hook in hooks)
        {
            await hook.AfterDispatchAsync(context, cancellationToken).ConfigureAwait(false);
        }
    }
}