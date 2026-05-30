using LiteBus.Amqp;
using Testcontainers.RabbitMq;

namespace LiteBus.Inbox.Ingress.Amqp.IntegrationTests;

/// <summary>
///     Shared RabbitMQ container for ingress integration tests.
/// </summary>
public sealed class RabbitMqBrokerFixture : IAsyncLifetime
{
    /// <summary>
    ///     Message shown when integration tests fail because Docker is not available.
    /// </summary>
    public const string DockerRequiredMessage =
        "AMQP integration tests require Docker. Start Docker Desktop (or the Docker daemon) and run the tests again.";

    private RabbitMqContainer? _container;

    /// <summary>
    ///     Gets the connection options for the started RabbitMQ container.
    /// </summary>
    public AmqpConnectionOptions ConnectionOptions { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        try
        {
            _container = new RabbitMqBuilder()
                .WithImage("rabbitmq:4-management")
                .WithUsername("guest")
                .WithPassword("guest")
                .Build();

            await _container.StartAsync();
            ConnectionOptions = new AmqpConnectionOptions
            {
                Uri = new Uri(_container.GetConnectionString()),
                ClientProvidedName = "LiteBus.Inbox.Ingress.Amqp.IntegrationTests.RabbitMQ"
            };
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(DockerRequiredMessage, exception);
        }
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
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

    private DotNet.Testcontainers.Containers.IContainer? _container;

    /// <summary>
    ///     Gets the connection options for the started LavinMQ container.
    /// </summary>
    public AmqpConnectionOptions ConnectionOptions { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        try
        {
            _container = new DotNet.Testcontainers.Builders.ContainerBuilder()
                .WithImage("cloudamqp/lavinmq")
                .WithPortBinding(5672, true)
                .WithWaitStrategy(DotNet.Testcontainers.Builders.Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(5672))
                .Build();

            await _container.StartAsync();
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

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }
}
