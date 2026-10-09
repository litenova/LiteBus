using LiteBus.Transport.Amqp;
using Testcontainers.RabbitMq;

namespace LiteBus.Durable.IntegrationTests.Dispatch.Outbox.Amqp;

/// <summary>
///     Shared RabbitMQ container for outbox AMQP dispatch integration tests.
/// </summary>
public sealed class RabbitMqFixture : IAsyncLifetime
{
    /// <summary>
    ///     Message shown when integration tests fail because Docker is not available.
    /// </summary>
    public const string DockerRequiredMessage =
        "Outbox AMQP integration tests require Docker. Start Docker Desktop (or the Docker daemon) and run the tests again.";

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
                    ClientProvidedName = "LiteBus.Durable.IntegrationTests.Dispatch.Outbox.Amqp.RabbitMQ"
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
