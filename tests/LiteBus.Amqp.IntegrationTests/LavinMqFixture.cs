using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using LiteBus.Amqp;

namespace LiteBus.Amqp.IntegrationTests;

/// <summary>
///     Shared LavinMQ container for integration tests. Initialization fails when Docker is unavailable.
/// </summary>
public sealed class LavinMqFixture : IAsyncLifetime
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

            await _container.StartAsync();
            ConnectionOptions = CreateConnectionOptions(_container);
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

    /// <summary>
    ///     Creates connection options from a running LavinMQ container.
    /// </summary>
    /// <param name="container">The started LavinMQ container.</param>
    /// <returns>Connection options that target the mapped AMQP port.</returns>
    private static AmqpConnectionOptions CreateConnectionOptions(IContainer container)
    {
        return new AmqpConnectionOptions
        {
            HostName = container.Hostname,
            Port = container.GetMappedPublicPort(5672),
            UserName = "guest",
            Password = "guest",
            VirtualHost = "/",
            ClientProvidedName = "LiteBus.Amqp.IntegrationTests.LavinMQ"
        };
    }
}
