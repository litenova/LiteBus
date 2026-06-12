using LiteBus.Transport.Amqp;
using Testcontainers.RabbitMq;

namespace LiteBus.DurableTransport.IntegrationTests.Amqp;

/// <summary>
///     Shared RabbitMQ container for durable transport integration tests.
/// </summary>
public sealed class RabbitMqBrokerFixture : IAsyncLifetime
{
    /// <summary>
    ///     The shared collection name for RabbitMQ-backed durable transport tests.
    /// </summary>
    public const string CollectionName = "DurableTransport.RabbitMq";

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

            await _container.StartAsync();

            ConnectionOptions = new AmqpConnectionOptions
            {
                Uri = new Uri(_container.GetConnectionString()),
                ClientProvidedName = "LiteBus.DurableTransport.IntegrationTests.RabbitMQ"
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
            await _container.DisposeAsync();
        }
    }
}
