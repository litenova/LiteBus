using LiteBus.Auditing.Abstractions;
using LiteBus.Auditing.Linking;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LiteBus.ConsumerSmoke;

/// <summary>
///     Drives the LiteBus host for the audit scenarios: fast link sweeps, a wait for the linked record, and shutdown.
/// </summary>
internal static class AuditSmokeHost
{
    /// <summary>
    ///     How long a scenario waits for the sweep to link its staged record.
    /// </summary>
    private static readonly TimeSpan LinkTimeout = TimeSpan.FromMinutes(2);

    /// <summary>
    ///     Starts the LiteBus hosted orchestrator with link sweeps every second for records of any age.
    /// </summary>
    /// <param name="provider">The composed service provider.</param>
    /// <returns>The started hosted service, to stop when the scenario ends.</returns>
    internal static async Task<IHostedService> StartHostAsync(IServiceProvider provider)
    {
        var sweep = provider.GetRequiredService<AuditLinkSweepHostOptions>();
        sweep.Interval = TimeSpan.FromSeconds(1);
        sweep.OlderThan = TimeSpan.Zero;

        var hosted = provider.GetRequiredService<IHostedService>();
        await hosted.StartAsync(CancellationToken.None).ConfigureAwait(false);
        return hosted;
    }

    /// <summary>
    ///     Polls one audit segment through a fresh scope per attempt until the sweep links the staged record.
    /// </summary>
    /// <typeparam name="TDocument">The stored audit document type of the scenario store.</typeparam>
    /// <param name="provider">The composed service provider the polling scopes hang off.</param>
    /// <param name="segment">The segment the scenario recorded into.</param>
    /// <returns>A task that completes when the segment holds exactly one linked record.</returns>
    internal static async Task WaitForLinkedRecordAsync<TDocument>(IServiceProvider provider, string segment)
        where TDocument : class, IAuditChainDocument
    {
        var deadline = DateTimeOffset.UtcNow.Add(LinkTimeout);

        while (true)
        {
            long linked;

            using (var scope = provider.CreateScope())
            {
                var store = scope.ServiceProvider.GetRequiredService<IAuditChainStore<TDocument>>();
                linked = await CountLinkedAsync(store, segment).ConfigureAwait(false);
            }

            if (linked == 1 || DateTimeOffset.UtcNow >= deadline)
            {
                Smoke.Expect(linked == 1, $"the audit segment '{segment}' holds one linked record, found {linked}");
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250)).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Counts the linked documents of one audit segment.
    /// </summary>
    /// <typeparam name="TDocument">The stored audit document type of the scenario store.</typeparam>
    /// <param name="store">The chain store to read through.</param>
    /// <param name="segment">The segment to count.</param>
    /// <returns>The number of linked documents.</returns>
    private static async Task<long> CountLinkedAsync<TDocument>(IAuditChainStore<TDocument> store, string segment)
        where TDocument : class, IAuditChainDocument
    {
        var count = 0L;

        await foreach (var document in store
            .ReadSegmentAsync(segment, 1, null, CancellationToken.None)
            .ConfigureAwait(false))
        {
            count++;
        }

        return count;
    }
}
