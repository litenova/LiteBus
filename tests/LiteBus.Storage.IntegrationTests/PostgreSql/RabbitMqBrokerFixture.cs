using LiteBus.Transport.Amqp;
using Testcontainers.RabbitMq;
using LiteBus.Storage.PostgreSql;
using LiteBus.Inbox;
using LiteBus.Outbox;
using LiteBus.Messaging;

namespace LiteBus.Storage.IntegrationTests.PostgreSql;

/// <summary>
///     Shared RabbitMQ container for PostgreSQL ingress integration tests.
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

    /// <summary>
    ///     Gets a value indicating whether the RabbitMQ container started successfully.
    /// </summary>
    public bool IsAvailable { get; private set; }

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        try
        {
            _container = new RabbitMqBuilder("rabbitmq:4-management")
                .WithUsername("guest")
                .WithPassword("guest")
                .Build();

            await _container.StartAsync().ConfigureAwait(false);

            ConnectionOptions = new AmqpConnectionOptions
            {
                Uri = new Uri(_container.GetConnectionString()),
                ClientProvidedName = "LiteBus.Storage.IntegrationTests.PostgreSql.RabbitMQ"
            };

            IsAvailable = true;
        }
        catch (Exception exception) when (DockerAvailability.IsDockerUnavailable(exception))
        {
            if (_container is not null)
            {
                await _container.DisposeAsync().ConfigureAwait(false);
                _container = null;
            }

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
