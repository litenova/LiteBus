using System.Runtime.CompilerServices;
using LiteBus.Inbox;
using LiteBus.Inbox.Ingress;
using LiteBus.Inbox.Ingress.Kafka;
using LiteBus.Runtime.Abstractions;
using LiteBus.Transport.Abstractions;
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
    private static readonly TimeSpan IngressWarmupDelay = TimeSpan.FromSeconds(1);

    /// <summary>
    ///     The maximum time allowed for ingress shutdown.
    /// </summary>
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(90);

    /// <summary>
    ///     Active ingress-only execution sessions keyed by test service provider instance.
    /// </summary>
    private static readonly ConditionalWeakTable<ServiceProvider, DirectKafkaIngressSession> IngressSessions = new();

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
    ///     Starts the Kafka ingress consumer loop and waits for subscription warmup.
    /// </summary>
    /// <param name="provider">The LiteBus service provider under test.</param>
    /// <returns>A task that completes when the ingress loop has started.</returns>
    public static async Task StartIngressAsync(ServiceProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        var session = DirectKafkaIngressSession.Create(provider);
        IngressSessions.Add(provider, session);

        await session.StartAsync().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        await Task.Delay(IngressWarmupDelay).ConfigureAwait(false);
    }

    /// <summary>
    ///     Starts ingress and inbox processor loops for end-to-end Kafka tests.
    /// </summary>
    /// <param name="provider">The LiteBus service provider under test.</param>
    /// <returns>A task that completes when background loops have started.</returns>
    public static async Task StartEndToEndAsync(ServiceProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        var session = new EndToEndSession(
            DirectKafkaIngressSession.Create(provider),
            provider.GetRequiredService<InboxProcessorBackgroundService>());

        EndToEndSessions.Add(provider, session);

        await session.StartAsync().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        await Task.Delay(IngressWarmupDelay).ConfigureAwait(false);
    }

    /// <summary>
    ///     Stops the Kafka ingress consumer loop started by <see cref="StartIngressAsync" />.
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
    ///     Runs the Kafka ingress consumer directly against <see cref="TransportInboxIngressHandler" />.
    /// </summary>
    private sealed class DirectKafkaIngressSession
    {
        /// <summary>
        ///     The transport consumer subscribed to the ingress topic.
        /// </summary>
        private readonly IMessageConsumer _consumer;

        /// <summary>
        ///     The handler that maps deliveries to inbox acceptance.
        /// </summary>
        private readonly TransportInboxIngressHandler _handler;

        /// <summary>
        ///     The ingress destination and acknowledgement settings.
        /// </summary>
        private readonly TransportInboxIngressOptions _options;

        /// <summary>
        ///     The cancellation source used to stop the consume loop.
        /// </summary>
        private readonly CancellationTokenSource _stoppingCts = new();

        /// <summary>
        ///     Initializes a new instance of the <see cref="DirectKafkaIngressSession" /> class.
        /// </summary>
        /// <param name="consumer">The transport consumer subscribed to the ingress topic.</param>
        /// <param name="handler">The handler that maps deliveries to inbox acceptance.</param>
        /// <param name="options">The ingress destination and acknowledgement settings.</param>
        private DirectKafkaIngressSession(
            IMessageConsumer consumer,
            TransportInboxIngressHandler handler,
            TransportInboxIngressOptions options)
        {
            ArgumentNullException.ThrowIfNull(consumer);
            _consumer = consumer;
            ArgumentNullException.ThrowIfNull(handler);
            _handler = handler;
            ArgumentNullException.ThrowIfNull(options);
            _options = options;
        }

        /// <summary>
        ///     Creates a session from services registered by the Kafka inbox ingress module.
        /// </summary>
        /// <param name="provider">The LiteBus service provider under test.</param>
        /// <returns>The configured ingress session.</returns>
        public static DirectKafkaIngressSession Create(ServiceProvider provider)
        {
            ArgumentNullException.ThrowIfNull(provider);

            return new DirectKafkaIngressSession(
                provider.GetRequiredService<IMessageConsumer>(),
                provider.GetRequiredService<TransportInboxIngressHandler>(),
                provider.GetRequiredService<TransportInboxIngressOptions>());
        }

        /// <summary>
        ///     Starts the Kafka consume loop.
        /// </summary>
        /// <returns>A task that completes when the consumer has subscribed.</returns>
        public Task StartAsync()
        {
            var consumerOptions = new TransportConsumerOptions
            {
                Destination = _options.Destination,
                PrefetchCount = _options.PrefetchCount,
                DeclareDestination = _options.DeclareDestination,
                DurableDestination = _options.DurableDestination
            };

            return _consumer.StartAsync(consumerOptions, HandleDeliveryAsync, _stoppingCts.Token);
        }

        /// <summary>
        ///     Stops the Kafka consume loop.
        /// </summary>
        /// <returns>A task that completes when the consumer has stopped or the stop timeout elapses.</returns>
        public async Task StopAsync()
        {
            await _stoppingCts.CancelAsync().ConfigureAwait(false);

            try
            {
                await _consumer.StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
            }
            finally
            {
                _stoppingCts.Dispose();
            }
        }

        /// <summary>
        ///     Accepts one transport delivery into the inbox and acknowledges the broker delivery.
        /// </summary>
        /// <param name="message">The received transport delivery.</param>
        /// <param name="cancellationToken">The token used to cancel acceptance.</param>
        /// <returns>A task that completes when the delivery has been acknowledged.</returns>
        private async Task HandleDeliveryAsync(TransportMessage message, CancellationToken cancellationToken)
        {
            try
            {
                await _handler.AcceptAsync(message, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (IngressAckPolicy.ShouldRequeue(exception, _options.RequeueOnFailure))
            {
                await message.ReturnToQueueAsync(cancellationToken).ConfigureAwait(false);
                return;
            }
#pragma warning disable CA1031 // Unknown contracts and poison payloads are discarded so the loop keeps running.
            catch (Exception)
            {
                await message.DiscardAsync(cancellationToken).ConfigureAwait(false);
                return;
            }
#pragma warning restore CA1031

            try
            {
                await message.AcceptAsync(cancellationToken).ConfigureAwait(false);
            }
#pragma warning disable CA1031 // Broker acknowledgement failures are requeued for idempotent redelivery.
            catch (Exception)
            {
                await message.ReturnToQueueAsync(cancellationToken).ConfigureAwait(false);
            }
#pragma warning restore CA1031
        }
    }

    /// <summary>
    ///     Tracks direct Kafka ingress and inbox processor loops for end-to-end tests.
    /// </summary>
    private sealed class EndToEndSession
    {
        /// <summary>
        ///     The direct Kafka ingress session.
        /// </summary>
        private readonly DirectKafkaIngressSession _ingressSession;

        /// <summary>
        ///     The inbox processor background service.
        /// </summary>
        private readonly InboxProcessorBackgroundService _processor;

        /// <summary>
        ///     The cancellation source used to stop the processor loop.
        /// </summary>
        private readonly CancellationTokenSource _processorStoppingCts = new();

        /// <summary>
        ///     The background task executing the inbox processor loop.
        /// </summary>
        private Task? _processorTask;

        /// <summary>
        ///     Initializes a new instance of the <see cref="EndToEndSession" /> class.
        /// </summary>
        /// <param name="ingressSession">The direct Kafka ingress session.</param>
        /// <param name="processor">The inbox processor background service.</param>
        public EndToEndSession(DirectKafkaIngressSession ingressSession, InboxProcessorBackgroundService processor)
        {
            ArgumentNullException.ThrowIfNull(ingressSession);
            _ingressSession = ingressSession;
            ArgumentNullException.ThrowIfNull(processor);
            _processor = processor;
        }

        /// <summary>
        ///     Starts ingress and processor loops.
        /// </summary>
        /// <returns>A task that completes when both loops have started.</returns>
        public async Task StartAsync()
        {
            await _ingressSession.StartAsync().ConfigureAwait(false);
            _processorTask = _processor.ExecuteAsync(_processorStoppingCts.Token);
        }

        /// <summary>
        ///     Stops ingress and processor loops.
        /// </summary>
        /// <returns>A task that completes when both loops have stopped or the stop timeout elapses.</returns>
        public async Task StopAsync()
        {
            await _processorStoppingCts.CancelAsync().ConfigureAwait(false);

            if (_processorTask is not null)
            {
                try
                {
                    await _processorTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                }
                catch (OperationCanceledException)
                {
                    // Expected when cancellation stops the processor loop.
                }
            }

            await _ingressSession.StopAsync().ConfigureAwait(false);
            _processorStoppingCts.Dispose();
        }
    }
}
