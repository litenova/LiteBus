using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.InMemory;
using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Outbox.UnitTests;

[Collection("Sequential")]
public sealed class OutboxHostingTests : LiteBusTestBase
{
    [Fact]
    public async Task ProcessorBackgroundService_WhenDisabled_ShouldCompleteWithoutPublishing()
    {
        var dispatcher = new OutboxTestInfrastructure.RecordingOutboxDispatcherHolder();

         var provider = BuildProvider(dispatcher, hostOptions => hostOptions.Enabled = false);
         await using (provider.ConfigureAwait(true))
         {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await OutboxTestInfrastructure.StartLiteBusHostedServicesAsync(provider, cts.Token).ConfigureAwait(true);
        await OutboxTestInfrastructure.StopLiteBusHostedServicesAsync(provider, CancellationToken.None).ConfigureAwait(true);

        dispatcher.Instance!.DispatchedMessages.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task ProcessorBackgroundService_ShouldPublishScheduledMessages()
    {
        var dispatcher = new OutboxTestInfrastructure.RecordingOutboxDispatcherHolder();

        var provider = BuildProvider(
            dispatcher,
            options => options.PollInterval = TimeSpan.FromMilliseconds(50));
        await using (provider.ConfigureAwait(true))
        {
        var outbox = provider.GetRequiredService<IOutbox>();

        var orderId = Guid.NewGuid();

        await outbox.EnqueueAsync(OutboxWriterTestFactory.ItemWithId(
            new OutboxTests.OrderSubmittedIntegrationEvent { OrderId = orderId },
            Guid.NewGuid())).ConfigureAwait(true);


        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await OutboxTestInfrastructure.StartLiteBusHostedServicesAsync(provider, cts.Token).ConfigureAwait(true);
        await Task.Delay(TimeSpan.FromMilliseconds(300)).ConfigureAwait(true);
        await OutboxTestInfrastructure.StopLiteBusHostedServicesAsync(provider, CancellationToken.None).ConfigureAwait(true);

        dispatcher.Instance!.DispatchedMessages
            .OfType<OutboxTests.OrderSubmittedIntegrationEvent>()
            .Should()
            .ContainSingle(submitted => submitted.OrderId == orderId);
        }
    }

    [Fact]
    public void ProcessorBackgroundService_WhenMissingDependency_ShouldThrowOnBuild()
    {
        var act = () =>
            new ServiceCollection()
                .AddLiteBus(registry =>
                {
                    registry.AddMessageModule(_ =>
                    {
                    });

                    registry.AddOutboxModule(outbox =>
                    {
                        outbox.Contracts.Register<OutboxTests.OrderSubmittedIntegrationEvent>("orders.events.submitted");
                        outbox.EnableOutboxProcessor();
                    });
                })
                .BuildServiceProvider();

        act.Should().Throw<LiteBusConfigurationException>()
            .WithMessage("*Outbox storage is required*");
    }

    [Fact]
    public void ProcessorBackgroundService_WhenMissingDispatcher_ShouldThrowOnBuild()
    {
        var act = () =>
            new ServiceCollection()
                .AddLiteBus(registry =>
                {
                    registry.AddMessageModule(_ =>
                    {
                    });

                    registry.AddOutboxModule(outbox =>
                    {
                        outbox.Contracts.Register<OutboxTests.OrderSubmittedIntegrationEvent>("orders.events.submitted");
                        outbox.UseInMemoryStorage();
                        outbox.EnableOutboxProcessor();
                    });
                })
                .BuildServiceProvider();

        act.Should().Throw<LiteBusConfigurationException>()
            .WithMessage("*EnableOutboxProcessor requires an outbox dispatcher*");
    }

    [Fact]
    public async Task ProcessorBackgroundService_ShouldRespectStartupDelay()
    {
        var dispatcher = new OutboxTestInfrastructure.RecordingOutboxDispatcherHolder();

        var provider = BuildProvider(
            dispatcher,
            options =>
            {
                options.StartupDelay = TimeSpan.FromMilliseconds(300);
                options.PollInterval = TimeSpan.FromMilliseconds(50);
            });
        await using (provider.ConfigureAwait(true))
        {
        var outbox = provider.GetRequiredService<IOutbox>();

        var orderId = Guid.NewGuid();

        await outbox.EnqueueAsync(OutboxWriterTestFactory.ItemWithId(
            new OutboxTests.OrderSubmittedIntegrationEvent { OrderId = orderId },
            Guid.NewGuid())).ConfigureAwait(true);


        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await OutboxTestInfrastructure.StartLiteBusHostedServicesAsync(provider, cts.Token).ConfigureAwait(true);

        await Task.Delay(TimeSpan.FromMilliseconds(100)).ConfigureAwait(true);
        dispatcher.Instance!.DispatchedMessages.Should().BeEmpty();

        await Task.Delay(TimeSpan.FromMilliseconds(350)).ConfigureAwait(true);
        await OutboxTestInfrastructure.StopLiteBusHostedServicesAsync(provider, CancellationToken.None).ConfigureAwait(true);

        dispatcher.Instance.DispatchedMessages
            .OfType<OutboxTests.OrderSubmittedIntegrationEvent>()
            .Should()
            .ContainSingle(submitted => submitted.OrderId == orderId);
        }
    }

    [Fact]
    public async Task ProcessorBackgroundService_WithAdaptivePollingAndFullBatch_ShouldPublishMultipleMessagesQuickly()
    {
        var dispatcher = new OutboxTestInfrastructure.RecordingOutboxDispatcherHolder();

        var provider = BuildProvider(
            dispatcher,
            configureOutbox: outbox =>
            {
                outbox.Contracts.Register<OutboxTests.OrderSubmittedIntegrationEvent>("orders.events.submitted");

                outbox.UseProcessorOptions(new OutboxProcessorOptions
                {
                    BatchSize = 2,
                    LeaseOwner = "test-publisher",
                    Retry = new RetryOptions { UseJitter = false }
                });
            },
            configureHost: options =>
            {
                options.UseAdaptivePolling = true;
                options.PollInterval = TimeSpan.FromMilliseconds(50);
            });
        await using (provider.ConfigureAwait(true))
        {
        var outbox = provider.GetRequiredService<IOutbox>();

        for (var i = 0; i < 4; i++)
        {
            await outbox.EnqueueAsync(OutboxWriterTestFactory.ItemWithId(
                new OutboxTests.OrderSubmittedIntegrationEvent { OrderId = Guid.NewGuid() },
                Guid.NewGuid())).ConfigureAwait(true);

        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await OutboxTestInfrastructure.StartLiteBusHostedServicesAsync(provider, cts.Token).ConfigureAwait(true);
        await WaitUntilAsync(() => dispatcher.Instance!.DispatchedMessages.Count == 4, TimeSpan.FromSeconds(10)).ConfigureAwait(true);
        await OutboxTestInfrastructure.StopLiteBusHostedServicesAsync(provider, CancellationToken.None).ConfigureAwait(true);

        dispatcher.Instance!.DispatchedMessages.Should().HaveCount(4);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;

        while (!condition() && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(50).ConfigureAwait(false);
        }
    }

    private static ServiceProvider BuildProvider(
        OutboxTestInfrastructure.RecordingOutboxDispatcherHolder dispatcherHolder,
        Action<OutboxProcessorHostOptions>? configureHost = null,
        Action<OutboxModuleBuilder>? configureOutbox = null)
    {
        return new ServiceCollection()
            .AddSingleton(dispatcherHolder)
            .AddLiteBus(registry =>
            {
                registry.AddMessageModule(_ =>
                {
                });

                registry.AddOutboxModule(outbox =>
                {
                    if (configureOutbox is not null)
                    {
                        configureOutbox(outbox);
                    }
                    else
                    {
                        outbox.Contracts.Register<OutboxTests.OrderSubmittedIntegrationEvent>("orders.events.submitted");

                        outbox.UseProcessorOptions(new OutboxProcessorOptions
                        {
                            BatchSize = 10,
                            LeaseOwner = "test-publisher",
                            Retry = new RetryOptions { UseJitter = false }
                        });
                    }

                    outbox.UseInMemoryStorage();
                    outbox.UseRecordingOutboxDispatcher(dispatcherHolder);
                    outbox.EnableOutboxProcessor(configureHost);
                });
            })
            .BuildServiceProvider();
    }
}
