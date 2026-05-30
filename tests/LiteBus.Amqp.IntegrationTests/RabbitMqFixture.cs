using LiteBus.Amqp;
using Testcontainers.RabbitMq;

namespace LiteBus.Amqp.IntegrationTests;

/// <summary>
///     Shared RabbitMQ container for integration tests. Initialization fails when Docker is unavailable.
/// </summary>
public sealed class RabbitMqFixture : IAsyncLifetime
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
            ClientProvidedName = "LiteBus.Amqp.IntegrationTests.RabbitMQ"
        };
    }
}
