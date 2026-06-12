using BenchmarkDotNet.Attributes;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging.Abstractions;
using LiteBus.Orchestration.Abstractions;

namespace LiteBus.Processor.Benchmarks;

/// <summary>
///     Measures pipelined <see cref="PipelinedInboxProcessor" /> throughput at realistic handler latencies.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class PipelinedInboxProcessorBenchmarks
{
    /// <summary>
    ///     The number of pending envelopes processed in each benchmark iteration.
    /// </summary>
    private const int BatchSize = 32;

    /// <summary>
    ///     The fixed UTC timestamp used for envelope creation.
    /// </summary>
    private static readonly DateTimeOffset BaseTime = new(2026, 6, 5, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    ///     The pipelined processor under measurement.
    /// </summary>
    private PipelinedInboxProcessor _processor = null!;

    /// <summary>
    ///     The inbox store seeded before each iteration.
    /// </summary>
    private InMemoryInboxStore _store = null!;

    /// <summary>
    ///     The handler latency applied by the benchmark dispatcher.
    /// </summary>
    [Params(1, 50, 500)]
    public int HandlerLatencyMilliseconds { get; set; }

    /// <summary>
    ///     The number of parallel dispatch workers.
    /// </summary>
    [Params(1, 4)]
    public int DispatcherConcurrency { get; set; }

    /// <summary>
    ///     Seeds the store and processor before each iteration.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _store = new InMemoryInboxStore();
        var dispatcher = new LatencyInboxDispatcher(TimeSpan.FromMilliseconds(HandlerLatencyMilliseconds));

        _processor = new PipelinedInboxProcessor(
            _store,
            _store,
            dispatcher,
            new InboxProcessorOptions
            {
                BatchSize = BatchSize,
                LeaseOwner = "benchmark-pipelined",
                LeaseDuration = TimeSpan.FromMinutes(5),
                DispatcherConcurrency = DispatcherConcurrency,
                LeaseHeartbeatInterval = TimeSpan.FromSeconds(15),
                Retry = new RetryOptions { MaxAttempts = 3, UseJitter = false }
            },
            TimeProvider.System,
            []);

        for (var index = 0; index < BatchSize; index++)
        {
            _store.AddAsync(new InboxEnvelope
            {
                Id = Guid.NewGuid(),
                ContractName = "benchmark.commands.ship",
                ContractVersion = 1,
                Payload = """{"index":0}""",
                CreatedAt = BaseTime.AddMilliseconds(index),
                Status = InboxStatus.Pending,
                AttemptCount = 0,
                IdempotencyKey = $"bench-pipe:{index}:{DispatcherConcurrency}"
            }).GetAwaiter().GetResult();
        }
    }

    /// <summary>
    ///     Processes one full batch through the pipelined inbox processor.
    /// </summary>
    /// <returns>A task that completes when the pass finishes.</returns>
    [Benchmark(Description = "Pipelined inbox processor pass")]
    public async Task ProcessPendingAsync()
    {
        await _processor.ProcessPendingAsync();
    }

    /// <summary>
    ///     Dispatcher that simulates handler work with a fixed delay.
    /// </summary>
    private sealed class LatencyInboxDispatcher : IInboxDispatcher
    {
        /// <summary>
        ///     The delay applied on every dispatch call.
        /// </summary>
        private readonly TimeSpan _latency;

        /// <summary>
        ///     Initializes a new instance of the <see cref="LatencyInboxDispatcher" /> class.
        /// </summary>
        /// <param name="latency">The delay applied on every dispatch call.</param>
        public LatencyInboxDispatcher(TimeSpan latency)
        {
            _latency = latency;
        }

        /// <inheritdoc />
        public async Task DispatchAsync(InboxEnvelope envelope, CancellationToken cancellationToken = default)
        {
            await Task.Delay(_latency, cancellationToken).ConfigureAwait(false);
        }
    }
}