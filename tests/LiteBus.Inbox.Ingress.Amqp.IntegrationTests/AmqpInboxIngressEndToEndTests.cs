using System.Collections.Generic;
using System.Text.Json;
using LiteBus.Amqp;
using LiteBus.Commands;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Dispatch.Commands;
using LiteBus.Inbox.Extensions.Microsoft.Hosting;
using LiteBus.Inbox.Ingress.Amqp;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging;
using LiteBus.Testing;
using LiteBus.Messaging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LiteBus.Inbox.Ingress.Amqp.IntegrationTests;

public sealed class AmqpInboxIngressEndToEndTests : LiteBusTestBase
{
    [Fact]
    public async Task PublishThroughRabbitMq_ShouldAcceptProcessAndDispatchCommand()
    {
        var fixture = new RabbitMqBrokerFixture();
        await fixture.InitializeAsync();

        try
        {
            await RunEndToEndAsync(fixture.ConnectionOptions);
        }
        finally
        {
            await fixture.DisposeAsync();
        }
    }

    [Fact]
    public async Task PublishThroughLavinMq_ShouldAcceptProcessAndDispatchCommand()
    {
        var fixture = new LavinMqBrokerFixture();
        await fixture.InitializeAsync();

        try
        {
            await RunEndToEndAsync(fixture.ConnectionOptions);
        }
        finally
        {
            await fixture.DisposeAsync();
        }
    }

    /// <summary>
    ///     Runs the publish, ingress, store, processor, and command dispatch flow against one broker.
    /// </summary>
    /// <param name="connectionOptions">The broker connection options.</param>
    /// <returns>A task that completes when the end-to-end flow succeeds.</returns>
    private static async Task RunEndToEndAsync(AmqpConnectionOptions connectionOptions)
    {
        const string queueNamePrefix = "litebus.inbox.ingress.tests";
        var queueName = $"{queueNamePrefix}.{Guid.NewGuid():N}";
        var recorder = new CommandRecorder();
        var orderId = Guid.NewGuid();

        var services = new ServiceCollection();
        services.AddSingleton(recorder);

        services.AddLiteBus(liteBus =>
        {
            liteBus.AddCommandModule(module =>
            {
                module.Register<ShipOrderCommand>();
                module.Register<ShipOrderCommandHandler>();
            });

            liteBus.AddInboxModule(inbox =>
            {
                inbox.Contracts.Register<ShipOrderCommand>("orders.commands.ship", 1);
                inbox.UseProcessorOptions(new InboxProcessorOptions
                {
                    BatchSize = 10,
                    LeaseOwner = "ingress-test-worker",
                    Retry = new RetryOptions { UseJitter = false }
                });
            });

            liteBus.AddInMemoryInboxStorage();
            liteBus.AddInboxCommandDispatcher();

            liteBus.AddInboxAmqpIngress(ingress =>
            {
                ingress.UseOptions(new AmqpInboxIngressOptions
                {
                    QueueName = queueName,
                    PrefetchCount = 1,
                    Connection = connectionOptions
                });
            });

            liteBus.AddInboxAmqpIngressHosting();
            liteBus.AddInboxProcessorHosting(host => host.PollInterval = TimeSpan.FromMilliseconds(100));
        });

        await using var provider = services.BuildServiceProvider();
        var hostedServices = provider.GetServices<IHostedService>().ToList();
        hostedServices.Should().HaveCount(2);

        using var runCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        foreach (var hostedService in hostedServices)
        {
            await hostedService.StartAsync(runCts.Token);
        }

        await Task.Delay(TimeSpan.FromSeconds(2), runCts.Token);

        try
        {
            var publisher = provider.GetRequiredService<IAmqpPublisher>();
            var command = new ShipOrderCommand { OrderId = orderId };
            var payload = JsonSerializer.SerializeToUtf8Bytes(command);

            await publisher.PublishAsync(new AmqpPublishRequest
            {
                Exchange = string.Empty,
                RoutingKey = queueName,
                Body = payload,
                Headers = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    [AmqpHeaders.MessageId] = Guid.NewGuid().ToString(),
                    [AmqpHeaders.ContractName] = "orders.commands.ship",
                    [AmqpHeaders.ContractVersion] = "1"
                }
            });

            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (DateTime.UtcNow < deadline)
            {
                if (recorder.Commands.Any(recorded => recorded.OrderId == orderId))
                {
                    break;
                }

                await Task.Delay(100);
            }

            recorder.Commands.Should().ContainSingle(recorded => recorded.OrderId == orderId);
        }
        finally
        {
            foreach (var hostedService in hostedServices)
            {
                await hostedService.StopAsync(CancellationToken.None);
            }
        }
    }
}
