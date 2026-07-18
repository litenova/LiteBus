using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.DurableMessaging.Abstractions.Processing;
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
    ///     Re-establishes hook-owned dispatch scope before the axis dispatcher runs.
    /// </summary>
    /// <param name="hooks">The registered orchestration hooks.</param>
    /// <param name="envelope">The leased outbox envelope.</param>
    internal static void RunPrepareDispatchScope(
        IReadOnlyList<IProcessorEnvelopeHook> hooks,
        OutboxEnvelope envelope)
    {
        var context = new OutboxProcessorEnvelopeAdapter(envelope);

        foreach (var hook in hooks)
        {
            hook.PrepareDispatchScope(context);
        }
    }

    /// <summary>
    ///     Determines whether every hook permits the axis dispatcher to run.
    /// </summary>
    /// <param name="hooks">The registered orchestration hooks.</param>
    /// <param name="envelope">The leased outbox envelope.</param>
    /// <returns><see langword="true" /> when every hook permits dispatch; otherwise, <see langword="false" />.</returns>
    internal static bool ShouldDispatch(
        IReadOnlyList<IProcessorEnvelopeHook> hooks,
        OutboxEnvelope envelope)
    {
        var context = new OutboxProcessorEnvelopeAdapter(envelope);
        var shouldDispatch = true;

        foreach (var hook in hooks)
        {
            shouldDispatch &= hook.ShouldDispatch(context);
        }

        return shouldDispatch;
    }

    /// <summary>
    ///     Releases hook-owned dispatch state after dispatch or after-dispatch processing stops on a failure.
    /// </summary>
    /// <param name="hooks">The registered orchestration hooks.</param>
    /// <param name="envelope">The leased outbox envelope.</param>
    internal static void RunAbandonDispatchScopes(
        IReadOnlyList<IProcessorEnvelopeHook> hooks,
        OutboxEnvelope envelope)
    {
        var context = new OutboxProcessorEnvelopeAdapter(envelope);

        foreach (var hook in hooks)
        {
            hook.AbandonDispatchScope(context);
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
