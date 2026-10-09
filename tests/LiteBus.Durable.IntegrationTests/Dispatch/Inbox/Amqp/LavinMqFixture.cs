using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using LiteBus.Transport.Amqp;
using LiteBus.Transport.IntegrationTesting;

namespace LiteBus.Durable.IntegrationTests.Dispatch.Inbox.Amqp;

/// <summary>
///     Shared LavinMQ container for inbox dispatch integration tests.
/// </summary>
public sealed class LavinMqFixture : IAsyncLifetime
{
    private IContainer? _container;

    /// <summary>
    ///     Gets the connection options for the started LavinMQ container.
    /// </summary>
    public AmqpConnectionOptions ConnectionOptions { get; private set; } = null!;

    /// <summary>
    ///     Gets a value indicating whether the LavinMQ container started successfully.
    /// </summary>
    public bool IsAvailable { get; private set; }

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        try
        {
            await DockerTestGate.RunAsync(async () =>
            {
                _container = new ContainerBuilder("cloudamqp/lavinmq")
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
                    ClientProvidedName = "LiteBus.Durable.IntegrationTests.Dispatch.Inbox.Amqp.LavinMQ"
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
