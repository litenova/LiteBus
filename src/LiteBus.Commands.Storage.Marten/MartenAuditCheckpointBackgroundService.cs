using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions.Auditing;
using LiteBus.Runtime.Abstractions;
using Marten;

namespace LiteBus.Commands.Storage.Marten;

/// <summary>
///     Cuts time-based audit checkpoints for segments that go quiet between writes.
/// </summary>
/// <remarks>
///     <para>
///         The chain asks its checkpoint policy after every append, but a policy that measures time can become due
///         without a new write. This loop asks the policy for each open segment on
///         <see cref="MartenAuditTrailOptions.CheckpointScanInterval" /> and cuts where it says due, so a quiet
///         segment still gets its checkpoint. <see cref="MartenAuditModule" /> registers the loop only when the
///         policy reports <see cref="ICheckpointPolicy.IsTimeBased" />.
///     </para>
///     <para>
///         A scan that fails is retried on the next interval, and one segment whose checkpoint fails never skips
///         the remaining segments. Verification surfaces a segment that never checkpoints as a missing anchor, so
///         a persistently failing segment is found there rather than in this loop.
///     </para>
/// </remarks>
public sealed class MartenAuditCheckpointBackgroundService : IBackgroundService
{
    /// <summary>
    ///     Gets the document store that opens the sessions scans and cuts run on.
    /// </summary>
    private readonly IDocumentStore _documentStore;

    /// <summary>
    ///     Gets the hash chain, segmenting, and checkpoint configuration.
    /// </summary>
    private readonly MartenAuditTrailOptions _options;

    /// <summary>
    ///     Gets the clock that measures scan intervals and stamps cut checkpoints.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    ///     Gets the provider that resolves pieces the builder named as types, or <see langword="null" />.
    /// </summary>
    private readonly IServiceProvider? _serviceProvider;

    /// <summary>
    ///     Initializes a new instance of the <see cref="MartenAuditCheckpointBackgroundService" /> class.
    /// </summary>
    /// <param name="documentStore">The application-owned Marten document store.</param>
    /// <param name="options">The hash chain configuration.</param>
    /// <param name="timeProvider">
    ///     The clock that measures scan intervals, or <see langword="null" /> for
    ///     <see cref="TimeProvider.System" />.
    /// </param>
    /// <param name="serviceProvider">
    ///     Resolves pieces the builder named as types, or <see langword="null" /> when every piece was given as an
    ///     instance.
    /// </param>
    public MartenAuditCheckpointBackgroundService(
        IDocumentStore documentStore,
        MartenAuditTrailOptions options,
        TimeProvider? timeProvider = null,
        IServiceProvider? serviceProvider = null)
    {
        ArgumentNullException.ThrowIfNull(documentStore);
        ArgumentNullException.ThrowIfNull(options);

        options.Validate();

        _documentStore = documentStore;
        _options = options;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _serviceProvider = serviceProvider;
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.ChainConfiguration is not { } chain || !chain.CheckpointPolicy.IsTimeBased)
        {
            return;
        }

        var checkpoints = new MartenAuditChain(_documentStore, _options, _timeProvider, _serviceProvider);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ScanOnceAsync(chain, checkpoints, stoppingToken).ConfigureAwait(false);
                await Task.Delay(_options.CheckpointScanInterval, _timeProvider, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
#pragma warning disable CA1031 // A checkpoint scan must survive any store failure and retry on the next interval.
            catch (Exception)
            {
                try
                {
                    await Task.Delay(_options.CheckpointScanInterval, _timeProvider, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
#pragma warning restore CA1031
        }
    }

    /// <summary>
    ///     Asks the checkpoint policy for each open segment and cuts where it says due.
    /// </summary>
    /// <param name="chain">The chain operations holding the policy.</param>
    /// <param name="checkpoints">The chain that cuts due checkpoints.</param>
    /// <param name="stoppingToken">The token used to stop the scan.</param>
    /// <returns>A task representing the asynchronous scan.</returns>
    private async Task ScanOnceAsync(
        MartenAuditChainConfiguration chain,
        MartenAuditChain checkpoints,
        CancellationToken stoppingToken)
    {
        var heads = await LoadOpenHeadsAsync(stoppingToken).ConfigureAwait(false);

        foreach (var head in heads)
        {
            stoppingToken.ThrowIfCancellationRequested();

            var candidate = new AuditCheckpointCandidate(
                head.Id,
                head.LastSequence,
                head.Count,
                head.LastCheckpointSequence,
                head.LastCheckpointAt,
                _timeProvider.GetUtcNow());

            if (!chain.CheckpointPolicy.IsDue(candidate))
            {
                continue;
            }

#pragma warning disable CA1031 // One segment whose checkpoint fails must not skip the remaining segments.
            try
            {
                await checkpoints.CutCheckpointAsync(head.Id, stoppingToken).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // The next scan retries the segment; verification surfaces one that never checkpoints.
            }
#pragma warning restore CA1031
        }
    }

    /// <summary>
    ///     Loads the heads of every segment that still accepts records.
    /// </summary>
    /// <param name="stoppingToken">The token used to stop the read.</param>
    /// <returns>The open segment heads.</returns>
    private async Task<IReadOnlyList<MartenAuditSegmentHead>> LoadOpenHeadsAsync(CancellationToken stoppingToken)
    {
        var session = _documentStore.QuerySession();

        await using (session.ConfigureAwait(false))
        {
            return await session
                .Query<MartenAuditSegmentHead>()
                .Where(head => !head.Expired)
                .ToListAsync(stoppingToken)
                .ConfigureAwait(false);
        }
    }
}
