using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using LiteBus.Amqp;

namespace LiteBus.Outbox.Dispatch.Amqp.IntegrationTests;

/// <summary>
///     Shared LavinMQ container for outbox AMQP dispatch integration tests.
/// </summary>
public sealed class LavinMqFixture : IAsyncLifetime
{
    /// <summary>
    ///     Message shown when integration tests fail because Docker is not available.
    /// </summary>
    public const string DockerRequiredMessage =
        "Outbox AMQP integration tests require Docker. Start Docker Desktop (or the Docker daemon) and run the tests again.";

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

            await _container.StartAsync();
            ConnectionOptions = new AmqpConnectionOptions
            {
                HostName = _container.Hostname,
                Port = _container.GetMappedPublicPort(5672),
                UserName = "guest",
                Password = "guest",
                VirtualHost = "/",
                ClientProvidedName = "LiteBus.Outbox.Dispatch.Amqp.IntegrationTests.LavinMQ"
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
            await _container.DisposeAsync();
        }
    }
}
