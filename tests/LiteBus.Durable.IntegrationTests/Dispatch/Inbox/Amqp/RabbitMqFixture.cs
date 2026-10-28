using LiteBus.Transport.Amqp;
using LiteBus.Transport.IntegrationTesting;
using Testcontainers.RabbitMq;

namespace LiteBus.Durable.IntegrationTests.Dispatch.Inbox.Amqp;

/// <summary>
///     Shared RabbitMQ container for inbox dispatch integration tests.
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

                ConnectionOptions = new AmqpConnectionOptions
                {
                    Uri = new Uri(_container.GetConnectionString()),
                    ClientProvidedName = "LiteBus.Durable.IntegrationTests.Dispatch.Inbox.Amqp.RabbitMQ"
                };

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
}
