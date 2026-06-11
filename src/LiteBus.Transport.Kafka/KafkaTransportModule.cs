using Confluent.Kafka;
using LiteBus.Runtime.Abstractions;
using LiteBus.Transport.Abstractions;

namespace LiteBus.Transport.Kafka;

/// <summary>
///     Module that registers Kafka transport services implementing <see cref="Abstractions.IMessageTransport" />.
/// </summary>
public sealed class KafkaTransportModule : IModule
{
    /// <summary>
    ///     Gets the connection settings configured by the application.
    /// </summary>
    private readonly KafkaTransportOptions _options;

    /// <summary>
    ///     Initializes a new instance of the <see cref="KafkaTransportModule" /> class.
    /// </summary>
    /// <param name="options">The connection settings configured by the application.</param>
    public KafkaTransportModule(KafkaTransportOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        ArgumentException.ThrowIfNullOrWhiteSpace(_options.BootstrapServers);
    }

    /// <inheritdoc />
    public void Build(IModuleConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (configuration.DependencyRegistry.Any(descriptor => descriptor.DependencyType == typeof(IMessageTransport)))
        {
            return;
        }

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(KafkaTransportOptions),
            _options));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IProducer<string, byte[]>),
            static serviceProvider =>
            {
                var options = serviceProvider.GetService(typeof(KafkaTransportOptions))
                                  as KafkaTransportOptions ??
                              throw new InvalidOperationException($"{nameof(KafkaTransportOptions)} is not registered.");

                var config = new ProducerConfig
                {
                    BootstrapServers = options.BootstrapServers,
                    ClientId = options.ClientId,
                    Acks = Acks.All
                };

                if (options.MessageTimeoutMs is int messageTimeoutMs)
                {
                    config.MessageTimeoutMs = messageTimeoutMs;
                    config.SocketTimeoutMs = messageTimeoutMs;
                    config.MessageSendMaxRetries = 0;
                }

                return new ProducerBuilder<string, byte[]>(config).Build();
            },
            InstanceLifetime.Singleton));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IConsumer<string, byte[]>),
            static serviceProvider =>
            {
                var options = serviceProvider.GetService(typeof(KafkaTransportOptions))
                                  as KafkaTransportOptions ??
                              throw new InvalidOperationException($"{nameof(KafkaTransportOptions)} is not registered.");

                var config = new ConsumerConfig
                {
                    BootstrapServers = options.BootstrapServers,
                    ClientId = options.ClientId,
                    GroupId = options.ConsumerGroupId,
                    EnableAutoCommit = false,
                    AutoOffsetReset = AutoOffsetReset.Earliest
                };

                return new ConsumerBuilder<string, byte[]>(config).Build();
            },
            InstanceLifetime.Singleton));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(ITransportCircuitBreaker),
            static _ => new TransportCircuitBreaker(),
            InstanceLifetime.Singleton));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IMessageTransport),
            typeof(KafkaPublisher)));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IMessageConsumer),
            typeof(KafkaConsumer)));

        TransportMetricsRegistration.RegisterIfNeeded(configuration);
    }
}