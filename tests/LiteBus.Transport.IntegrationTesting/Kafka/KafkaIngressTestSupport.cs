using System.Runtime.CompilerServices;
using LiteBus.Inbox;
using LiteBus.Inbox.Ingress;
using LiteBus.Inbox.Ingress.Kafka;
using LiteBus.Transport.Kafka;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Transport.IntegrationTesting.Kafka;

/// <summary>
///     Shared helpers for Kafka inbox ingress integration tests.
/// </summary>
public static class KafkaIngressTestSupport
{
    /// <summary>
    ///     The delay applied after the ingress loop starts so the consumer can subscribe.
    /// </summary>
    private static readonly TimeSpan IngressWarmupDelay = TimeSpan.FromSeconds(2);

    /// <summary>
    ///     The maximum time allowed for ingress shutdown.
    /// </summary>
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(90);

    /// <summary>
    ///     Active ingress-only execution sessions keyed by test service provider instance.
    /// </summary>
    private static readonly ConditionalWeakTable<ServiceProvider, IngressConsumerSession> IngressSessions = new();

    /// <summary>
    ///     Active end-to-end execution sessions keyed by test service provider instance.
    /// </summary>
    private static readonly ConditionalWeakTable<ServiceProvider, EndToEndSession> EndToEndSessions = new();

    /// <summary>
    ///     Creates Kafka connection settings with an isolated consumer group for one test case.
    /// </summary>
    /// <param name="transportOptions">The shared Kafka broker transport options.</param>
    /// <returns>Connection settings safe for parallel ingress scenarios.</returns>
    public static KafkaTransportOptions CreateConnection(KafkaTransportOptions transportOptions)
    {
        ArgumentNullException.ThrowIfNull(transportOptions);

        return new KafkaTransportOptions
        {
            BootstrapServers = transportOptions.BootstrapServers,
            ClientId = transportOptions.ClientId,
            ConsumerGroupId = $"litebus-ingress-{Guid.NewGuid():N}",
            MessageTimeoutMs = 10_000
        };
    }

    /// <summary>
    ///     Applies test-friendly host settings to a Kafka ingress module builder.
    /// </summary>
    /// <param name="ingress">The Kafka ingress module builder.</param>
    public static void ConfigureTestIngress(KafkaInboxIngressModuleBuilder ingress)
    {
        ArgumentNullException.ThrowIfNull(ingress);

        ingress.ConfigureHost(host => host.RetryPollInterval = TimeSpan.Zero);
    }

    /// <summary>
    ///     Starts the production <see cref="TransportInboxIngressConsumer" /> loop for ingress-only tests.
    /// </summary>
    /// <param name="provider">The LiteBus service provider under test.</param>
    /// <returns>A task that completes when the ingress loop has started.</returns>
    public static async Task StartIngressAsync(ServiceProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        var session = IngressConsumerSession.Start(provider);
        IngressSessions.Add(provider, session);

        await Task.Delay(IngressWarmupDelay).ConfigureAwait(false);
    }

    /// <summary>
    ///     Stops the ingress loop started by <see cref="StartIngressAsync" />.
    /// </summary>
    /// <param name="provider">The LiteBus service provider under test.</param>
    /// <returns>A task that completes when the ingress loop has stopped or the stop timeout elapses.</returns>
    public static Task StopIngressAsync(ServiceProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        if (IngressSessions.TryGetValue(provider, out var session))
        {
            return session.StopAsync();
        }

        return Task.CompletedTask;
    }

    /// <summary>
    ///     Starts ingress and inbox processor loops for end-to-end Kafka tests.
    /// </summary>
    /// <param name="provider">The LiteBus service provider under test.</param>
    /// <returns>A task that completes when background loops have started.</returns>
    public static async Task StartEndToEndAsync(ServiceProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        var session = EndToEndSession.Start(provider);
        EndToEndSessions.Add(provider, session);

        await Task.Delay(IngressWarmupDelay).ConfigureAwait(false);
    }

    /// <summary>
    ///     Stops background loops started by <see cref="StartEndToEndAsync" />.
    /// </summary>
    /// <param name="provider">The LiteBus service provider under test.</param>
    /// <returns>A task that completes when background loops have stopped or the stop timeout elapses.</returns>
    public static Task StopEndToEndAsync(ServiceProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        if (EndToEndSessions.TryGetValue(provider, out var session))
        {
            return session.StopAsync();
        }

        return StopIngressAsync(provider);
    }

    /// <summary>
    ///     Starts every manifest-hosted service registered by <c>AddLiteBus</c> for end-to-end tests.
    /// </summary>
    /// <param name="provider">The LiteBus service provider under test.</param>
    /// <returns>A task that completes when hosted services have started.</returns>
    public static Task StartHostedServicesAsync(ServiceProvider provider)
    {
        return StartEndToEndAsync(provider);
    }

    /// <summary>
    ///     Stops every manifest-hosted service registered by <c>AddLiteBus</c>.
    /// </summary>
    /// <param name="provider">The LiteBus service provider under test.</param>
    /// <returns>A task that completes when hosted services have stopped or the stop timeout elapses.</returns>
    public static Task StopHostedServicesAsync(ServiceProvider provider)
    {
        return StopEndToEndAsync(provider);
    }

    /// <summary>
    ///     Runs <see cref="TransportInboxIngressConsumer" /> for ingress-only integration tests.
    /// </summary>
    private sealed class IngressConsumerSession
    {
        /// <summary>
        ///     The cancellation source used to stop the ingress loop.
        /// </summary>
        private readonly CancellationTokenSource _stoppingCts = new();

        /// <summary>
        ///     The background task executing the ingress consumer loop.
        /// </summary>
        private Task? _executionTask;

        /// <summary>
        ///     Starts the ingress consumer loop from the registered service provider.
        /// </summary>
        /// <param name="provider">The LiteBus service provider under test.</param>
        /// <returns>The session tracking the running ingress loop.</returns>
        public static IngressConsumerSession Start(ServiceProvider provider)
        {
            ArgumentNullException.ThrowIfNull(provider);

            var session = new IngressConsumerSession();
            var consumer = provider.GetRequiredService<TransportInboxIngressConsumer>();
            session._executionTask = Task.Run(
                () => consumer.ExecuteAsync(session._stoppingCts.Token),
                session._stoppingCts.Token);
            return session;
        }

        /// <summary>
        ///     Stops the ingress consumer loop.
        /// </summary>
        /// <returns>A task that completes when the loop has stopped or the stop timeout elapses.</returns>
        public async Task StopAsync()
        {
            await _stoppingCts.CancelAsync().ConfigureAwait(false);

            if (_executionTask is not null)
            {
                try
                {
                    await _executionTask.WaitAsync(StopTimeout).ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                }
                catch (OperationCanceledException)
                {
                    // Expected when cancellation stops the ingress loop.
                }
            }

            _stoppingCts.Dispose();
        }
    }

    /// <summary>
    ///     Tracks ingress and inbox processor loops for end-to-end Kafka tests.
    /// </summary>
    private sealed class EndToEndSession
    {
        /// <summary>
        ///     The cancellation source used to stop background loops.
        /// </summary>
        private readonly CancellationTokenSource _stoppingCts = new();

        /// <summary>
        ///     The background task executing the ingress consumer loop.
        /// </summary>
        private readonly Task _ingressTask;

        /// <summary>
        ///     The background task executing the inbox processor loop.
        /// </summary>
        private readonly Task _processorTask;

        /// <summary>
        ///     Initializes a new instance of the <see cref="EndToEndSession" /> class.
        /// </summary>
        /// <param name="ingressTask">The ingress consumer loop task.</param>
        /// <param name="processorTask">The inbox processor loop task.</param>
        /// <param name="stoppingCts">The cancellation source used to stop both loops.</param>
        private EndToEndSession(Task ingressTask, Task processorTask, CancellationTokenSource stoppingCts)
        {
            _ingressTask = ingressTask;
            _processorTask = processorTask;
            _stoppingCts = stoppingCts;
        }

        /// <summary>
        ///     Starts ingress and processor loops from the registered service provider.
        /// </summary>
        /// <param name="provider">The LiteBus service provider under test.</param>
        /// <returns>The session tracking both background loops.</returns>
        public static EndToEndSession Start(ServiceProvider provider)
        {
            ArgumentNullException.ThrowIfNull(provider);

            var stoppingCts = new CancellationTokenSource();
            var ingress = provider.GetRequiredService<TransportInboxIngressConsumer>();
            var processor = provider.GetRequiredService<InboxProcessorBackgroundService>();

            var ingressTask = Task.Run(
                () => ingress.ExecuteAsync(stoppingCts.Token),
                stoppingCts.Token);
            var processorTask = Task.Run(
                () => processor.ExecuteAsync(stoppingCts.Token),
                stoppingCts.Token);

            return new EndToEndSession(ingressTask, processorTask, stoppingCts);
        }

        /// <summary>
        ///     Stops ingress and processor loops.
        /// </summary>
        /// <returns>A task that completes when both loops have stopped or the stop timeout elapses.</returns>
        public async Task StopAsync()
        {
            await _stoppingCts.CancelAsync().ConfigureAwait(false);

            try
            {
                await Task.WhenAll(_ingressTask, _processorTask).WaitAsync(StopTimeout).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
            }
            catch (OperationCanceledException)
            {
                // Expected when cancellation stops the background loops.
            }

            _stoppingCts.Dispose();
        }
    }
}
