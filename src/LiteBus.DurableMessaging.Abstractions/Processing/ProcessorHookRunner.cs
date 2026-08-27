using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.DurableMessaging.Abstractions.Processing;

/// <summary>
///     Invokes orchestration processor envelope hooks around a durable dispatch.
/// </summary>
/// <remarks>
///     The inbox and outbox ran identical copies of this until v7. Both take an <see cref="IProcessorEnvelope" /> rather
///     than an axis envelope, so the runner never learns which axis it is serving and one copy serves both. Each caller
///     adapts its envelope once per dispatch and passes the result through every phase, rather than adapting again in
///     each of them.
/// </remarks>
internal static class ProcessorHookRunner
{
    /// <summary>
    ///     Runs all hooks before dispatch begins.
    /// </summary>
    /// <param name="hooks">The registered orchestration hooks.</param>
    /// <param name="envelope">The leased envelope being dispatched.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>A task that completes before dispatch begins.</returns>
    internal static async Task RunBeforeDispatchAsync(
        IReadOnlyList<IProcessorEnvelopeHook> hooks,
        IProcessorEnvelope envelope,
        CancellationToken cancellationToken)
    {
        foreach (var hook in hooks)
        {
            await hook.BeforeDispatchAsync(envelope, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Re-establishes hook-owned dispatch scope before the axis dispatcher runs.
    /// </summary>
    /// <param name="hooks">The registered orchestration hooks.</param>
    /// <param name="envelope">The leased envelope being dispatched.</param>
    internal static void RunPrepareDispatchScope(
        IReadOnlyList<IProcessorEnvelopeHook> hooks,
        IProcessorEnvelope envelope)
    {
        foreach (var hook in hooks)
        {
            hook.PrepareDispatchScope(envelope);
        }
    }

    /// <summary>
    ///     Determines whether every hook permits the axis dispatcher to run.
    /// </summary>
    /// <param name="hooks">The registered orchestration hooks.</param>
    /// <param name="envelope">The leased envelope being dispatched.</param>
    /// <returns><see langword="true" /> when every hook permits dispatch; otherwise, <see langword="false" />.</returns>
    /// <remarks>
    ///     Every hook is asked even once one has declined, because a hook may be establishing state it expects to
    ///     release in <see cref="RunAbandonDispatchScopes" />.
    /// </remarks>
    internal static bool ShouldDispatch(
        IReadOnlyList<IProcessorEnvelopeHook> hooks,
        IProcessorEnvelope envelope)
    {
        var shouldDispatch = true;

        foreach (var hook in hooks)
        {
            shouldDispatch &= hook.ShouldDispatch(envelope);
        }

        return shouldDispatch;
    }

    /// <summary>
    ///     Releases hook-owned dispatch state after dispatch or after-dispatch processing stops on a failure.
    /// </summary>
    /// <param name="hooks">The registered orchestration hooks.</param>
    /// <param name="envelope">The leased envelope being dispatched.</param>
    internal static void RunAbandonDispatchScopes(
        IReadOnlyList<IProcessorEnvelopeHook> hooks,
        IProcessorEnvelope envelope)
    {
        foreach (var hook in hooks)
        {
            hook.AbandonDispatchScope(envelope);
        }
    }

    /// <summary>
    ///     Runs all hooks after dispatch completes successfully and terminal state is persisted.
    /// </summary>
    /// <param name="hooks">The registered orchestration hooks.</param>
    /// <param name="envelope">The leased envelope being dispatched.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>A task that completes after hook post-processing finishes.</returns>
    internal static async Task RunAfterDispatchAsync(
        IReadOnlyList<IProcessorEnvelopeHook> hooks,
        IProcessorEnvelope envelope,
        CancellationToken cancellationToken)
    {
        foreach (var hook in hooks)
        {
            await hook.AfterDispatchAsync(envelope, cancellationToken).ConfigureAwait(false);
        }
    }
}
