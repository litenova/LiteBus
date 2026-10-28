using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Inbox.Abstractions;
using LiteBus.Messaging.Abstractions.Processing;
using LiteBus.Outbox.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Testing;

/// <summary>
///     Lightweight helpers for running inbox or outbox processor passes in tests.
/// </summary>
public static class ProcessorPassHelpers
{
    /// <summary>
    ///     Resolves <see cref="IInboxProcessor" /> from the provider and runs one processing pass.
    /// </summary>
    /// <param name="provider">The service provider built with an enabled inbox processor.</param>
    /// <param name="cancellationToken">A token that cancels the pass.</param>
    /// <returns>The pass result describing leased, succeeded, failed, and dead-lettered counts.</returns>
    public static Task<ProcessorPassResult> RunInboxPassAsync(
        IServiceProvider provider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return provider.GetRequiredService<IInboxProcessor>().ProcessPendingAsync(cancellationToken);
    }

    /// <summary>
    ///     Resolves <see cref="IOutboxProcessor" /> from the provider and runs one publishing pass.
    /// </summary>
    /// <param name="provider">The service provider built with an enabled outbox processor.</param>
    /// <param name="cancellationToken">A token that cancels the pass.</param>
    /// <returns>The pass result describing leased, succeeded, failed, and dead-lettered counts.</returns>
    public static Task<ProcessorPassResult> RunOutboxPassAsync(
        IServiceProvider provider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return provider.GetRequiredService<IOutboxProcessor>().ProcessPendingAsync(cancellationToken);
    }

    /// <summary>
    ///     Resolves <see cref="IInboxProcessor" /> from the provider and runs passes until one pass leases nothing.
    /// </summary>
    /// <param name="provider">The service provider built with an enabled inbox processor.</param>
    /// <param name="maxPasses">The maximum number of passes before the drain fails.</param>
    /// <param name="cancellationToken">A token that cancels the drain.</param>
    /// <returns>The aggregate drain result across every executed pass.</returns>
    /// <remarks>
    ///     The drain loops without polling waits, so it returns immediately once no visible work remains. Rows that
    ///     failed with a future visibility timestamp are not leased until the store clock reaches them; advance a
    ///     manual clock and drain again to process them.
    /// </remarks>
    public static Task<ProcessorDrainResult> DrainInboxAsync(
        IServiceProvider provider,
        int maxPasses = 100,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxPasses, 0);

        return DrainAsync(provider.GetRequiredService<IInboxProcessor>(), "Inbox", maxPasses, cancellationToken);
    }

    /// <summary>
    ///     Resolves <see cref="IOutboxProcessor" /> from the provider and runs passes until one pass leases nothing.
    /// </summary>
    /// <param name="provider">The service provider built with an enabled outbox processor.</param>
    /// <param name="maxPasses">The maximum number of passes before the drain fails.</param>
    /// <param name="cancellationToken">A token that cancels the drain.</param>
    /// <returns>The aggregate drain result across every executed pass.</returns>
    /// <remarks>
    ///     The drain loops without polling waits, so it returns immediately once no visible work remains. Rows that
    ///     failed with a future visibility timestamp are not leased until the store clock reaches them; advance a
    ///     manual clock and drain again to process them.
    /// </remarks>
    public static Task<ProcessorDrainResult> DrainOutboxAsync(
        IServiceProvider provider,
        int maxPasses = 100,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxPasses, 0);

        return DrainAsync(provider.GetRequiredService<IOutboxProcessor>(), "Outbox", maxPasses, cancellationToken);
    }

    /// <summary>
    ///     Runs processor passes until one pass leases nothing and aggregates the pass results.
    /// </summary>
    /// <param name="processor">The inbox or outbox processor that executes each pass.</param>
    /// <param name="axisName">The axis name used in the guard exception when the drain does not settle.</param>
    /// <param name="maxPasses">The maximum number of passes before the drain fails.</param>
    /// <param name="cancellationToken">A token that cancels the drain.</param>
    /// <returns>The aggregate drain result across every executed pass.</returns>
    private static async Task<ProcessorDrainResult> DrainAsync(
        IMessageProcessor processor,
        string axisName,
        int maxPasses,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var passes = 0;
        var leasedCount = 0;
        var succeededCount = 0;
        var failedCount = 0;
        var deadLetteredCount = 0;

        while (true)
        {
            var pass = await processor.ProcessPendingAsync(cancellationToken).ConfigureAwait(false);
            passes++;
            leasedCount += pass.LeasedCount;
            succeededCount += pass.SucceededCount;
            failedCount += pass.FailedCount;
            deadLetteredCount += pass.DeadLetteredCount;

            if (pass.LeasedCount == 0)
            {
                break;
            }

            if (passes >= maxPasses)
            {
                throw new InvalidOperationException(
                    $"{axisName} drain ran {maxPasses} passes and the last pass still leased messages. " +
                    "Increase maxPasses, or check retry and backoff configuration for messages that never settle.");
            }
        }

        stopwatch.Stop();

        return new ProcessorDrainResult
        {
            Passes = passes,
            LeasedCount = leasedCount,
            SucceededCount = succeededCount,
            FailedCount = failedCount,
            DeadLetteredCount = deadLetteredCount,
            ElapsedTime = stopwatch.Elapsed
        };
    }
}
