using Testcontainers.RabbitMq;

namespace LiteBus.Transport.IntegrationTests.Amqp;

/// <summary>
///     Shared RabbitMQ container for integration tests. Only container-runtime failures map to the Docker-required outcome.
/// </summary>
public sealed class RabbitMqFixture : IAsyncLifetime
{
    private RabbitMqContainer? _container;

    /// <summary>
    ///     Gets the connection options for the started RabbitMQ container.
    /// </summary>
    public AmqpConnectionOptions ConnectionOptions { get; private set; } = null!;

    /// <summary>
    ///     Gets a value indicating whether the RabbitMQ container started successfully.
    /// </summary>
    public bool IsAvailable { get; private set; }

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        try
        {
            await DockerTestGate.RunAsync(async () =>
            {
                _container = new RabbitMqBuilder("rabbitmq:4-management")
                    .WithUsername("guest")
                    .WithPassword("guest")
                    .Build();

                await _container.StartAsync().ConfigureAwait(false);
                ConnectionOptions = CreateConnectionOptions(_container);
                IsAvailable = true;
            }).ConfigureAwait(false);
        }
        catch (InvalidOperationException) when (!DockerTestGate.IsStrictTransportMode)
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

    /// <summary>
    ///     Creates connection options from a running RabbitMQ container.
    /// </summary>
    /// <param name="container">The started RabbitMQ container.</param>
    /// <returns>Connection options that target the mapped AMQP port.</returns>
    private static AmqpConnectionOptions CreateConnectionOptions(RabbitMqContainer container)
    {
        var connectionUri = new Uri(container.GetConnectionString());

        return new AmqpConnectionOptions
        {
            Uri = connectionUri,
            ClientProvidedName = "LiteBus.Transport.IntegrationTests.Amqp.RabbitMQ"
        };
    }
}
