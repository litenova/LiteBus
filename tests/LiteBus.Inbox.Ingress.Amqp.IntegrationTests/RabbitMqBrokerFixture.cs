using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using LiteBus.Transport.Amqp;
using Testcontainers.RabbitMq;

namespace LiteBus.Inbox.Ingress.Amqp.IntegrationTests;

/// <summary>
///     Shared RabbitMQ container for durable transport integration tests.
/// </summary>
public sealed class RabbitMqBrokerFixture : IAsyncLifetime
{
    /// <summary>
    ///     The shared collection name for RabbitMQ-backed durable transport tests.
    /// </summary>
    public const string CollectionName = "Inbox.Ingress.Amqp.RabbitMq";

    private RabbitMqContainer? _container;

    /// <summary>
    ///     Gets whether the RabbitMQ container started successfully.
    /// </summary>
    public bool IsAvailable { get; private set; }

    /// <summary>
    ///     Gets the connection options for the started RabbitMQ container.
    /// </summary>
    public AmqpConnectionOptions ConnectionOptions { get; private set; } = null!;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        try
        {
            _container = new RabbitMqBuilder()
                .WithImage("rabbitmq:4-management")
                .WithUsername("guest")
                .WithPassword("guest")
                .Build();

            await _container.StartAsync().ConfigureAwait(false);

            ConnectionOptions = new AmqpConnectionOptions
            {
                Uri = new Uri(_container.GetConnectionString()),
                ClientProvidedName = "LiteBus.Inbox.Ingress.Amqp.IntegrationTests.RabbitMQ"
            };

            IsAvailable = true;
        }
        catch
        {
            IsAvailable = false;
        }
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync().ConfigureAwait(false);
        }
    }
}

/// <summary>
///     Shared LavinMQ container for ingress integration tests.
/// </summary>
public sealed class LavinMqBrokerFixture : IAsyncLifetime
{
    /// <summary>
    ///     Message shown when integration tests fail because Docker is not available.
    /// </summary>
    public const string DockerRequiredMessage =
        "AMQP integration tests require Docker. Start Docker Desktop (or the Docker daemon) and run the tests again.";

    private IContainer? _container;

    /// <summary>
    ///     Gets the connection options for the started LavinMQ container.
    /// </summary>
    public AmqpConnectionOptions ConnectionOptions { get; private set; } = null!;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        try
        {
            _container = new ContainerBuilder()
                .WithImage("cloudamqp/lavinmq")
                .WithPortBinding(5672, true)
                .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(5672))
                .Build();

            await _container.StartAsync().ConfigureAwait(false);

            ConnectionOptions = new AmqpConnectionOptions
            {
                HostName = _container.Hostname,
                Port = _container.GetMappedPublicPort(5672),
                UserName = "guest",
                Password = "guest",
                VirtualHost = "/",
                ClientProvidedName = "LiteBus.Inbox.Ingress.Amqp.IntegrationTests.LavinMQ"
            };
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(DockerRequiredMessage, exception);
        }
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync().ConfigureAwait(false);
        }
    }
}
