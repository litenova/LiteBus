using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox;
using LiteBus.Outbox.Abstractions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LiteBus.Outbox.UnitTests;

[Collection("Sequential")]
public sealed class OutboxHostingTests : LiteBusTestBase
{
    [Fact]
    public async Task ProcessorBackgroundWork_WhenDisabled_ShouldCompleteWithoutPublishing()
    {
        var store = new OutboxTests.InMemoryOutboxStore();
        var dispatcher = new OutboxTestInfrastructure.RecordingOutboxDispatcherHolder();

        await using var provider = BuildProvider(store, dispatcher, hostOptions => hostOptions.Enabled = false);
        var hostedService = provider.GetServices<IHostedService>().Single();

        await hostedService.StartAsync(CancellationToken.None);
        await hostedService.StopAsync(CancellationToken.None);

        dispatcher.Instance!.DispatchedMessages.Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessorBackgroundWork_ShouldPublishScheduledMessages()
    {
        var store = new OutboxTests.InMemoryOutboxStore();
        var dispatcher = new OutboxTestInfrastructure.RecordingOutboxDispatcherHolder();

        await using var provider = BuildProvider(
            store,
            dispatcher,
            configureHost: options => options.PollInterval = TimeSpan.FromMilliseconds(50));

        var outbox = provider.GetRequiredService<IOutbox>();
        var hostedService = provider.GetServices<IHostedService>().Single();

        var orderId = Guid.NewGuid();
        await outbox.AddAsync(new OutboxTests.OrderSubmittedIntegrationEvent { OrderId = orderId }, new OutboxOptions { Id = Guid.NewGuid() });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await hostedService.StartAsync(cts.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(300));
        await hostedService.StopAsync(CancellationToken.None);

        dispatcher.Instance!.DispatchedMessages
            .OfType<OutboxTests.OrderSubmittedIntegrationEvent>()
            .Should()
            .ContainSingle(submitted => submitted.OrderId == orderId);
    }

    [Fact]
    public async Task ProcessorBackgroundWork_WhenMissingDependency_ShouldThrowOnResolve()
    {
        var services = new ServiceCollection()
            .AddLiteBus(configuration =>
            {
                configuration.AddOutboxModule(outbox =>
                {
                    outbox.Contracts.Register<OutboxTests.OrderSubmittedIntegrationEvent>("orders.events.submitted", 1);
                    outbox.UseProcessorBackgroundWork();
                });
            });

        await using var provider = services.BuildServiceProvider();

        var act = () => provider.GetServices<IHostedService>().ToList();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*IOutboxLeaseStore*");
    }

    [Fact]
    public async Task ProcessorBackgroundWork_WhenMissingDispatcher_ShouldThrowOnResolve()
    {
        var store = new OutboxTests.InMemoryOutboxStore();

        var services = new ServiceCollection()
            .AddSingleton<IOutboxStore>(store)
            .AddSingleton<IOutboxLeaseStore>(store)
            .AddSingleton<IOutboxStateStore>(store)
            .AddLiteBus(configuration =>
            {
                configuration.AddOutboxModule(outbox =>
                {
                    outbox.Contracts.Register<OutboxTests.OrderSubmittedIntegrationEvent>("orders.events.submitted", 1);
                    outbox.UseProcessorBackgroundWork();
                });
            });

        await using var provider = services.BuildServiceProvider();

        var act = () => provider.GetServices<IHostedService>().ToList();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*IOutboxDispatcher*");
    }

    [Fact]
    public async Task ProcessorBackgroundWork_ShouldRespectStartupDelay()
    {
        var store = new OutboxTests.InMemoryOutboxStore();
        var dispatcher = new OutboxTestInfrastructure.RecordingOutboxDispatcherHolder();

        await using var provider = BuildProvider(
            store,
            dispatcher,
            configureHost: options =>
            {
                options.StartupDelay = TimeSpan.FromMilliseconds(300);
                options.PollInterval = TimeSpan.FromMilliseconds(50);
            });

        var outbox = provider.GetRequiredService<IOutbox>();
        var hostedService = provider.GetServices<IHostedService>().Single();

        var orderId = Guid.NewGuid();
        await outbox.AddAsync(new OutboxTests.OrderSubmittedIntegrationEvent { OrderId = orderId }, new OutboxOptions { Id = Guid.NewGuid() });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await hostedService.StartAsync(cts.Token);

        await Task.Delay(TimeSpan.FromMilliseconds(100));
        dispatcher.Instance!.DispatchedMessages.Should().BeEmpty();

        await Task.Delay(TimeSpan.FromMilliseconds(350));
        await hostedService.StopAsync(CancellationToken.None);

        dispatcher.Instance.DispatchedMessages
            .OfType<OutboxTests.OrderSubmittedIntegrationEvent>()
            .Should()
            .ContainSingle(submitted => submitted.OrderId == orderId);
    }

    [Fact]
    public async Task ProcessorBackgroundWork_WithAdaptivePollingAndFullBatch_ShouldPublishMultipleMessagesQuickly()
    {
        var store = new OutboxTests.InMemoryOutboxStore();
        var dispatcher = new OutboxTestInfrastructure.RecordingOutboxDispatcherHolder();

        await using var provider = BuildProvider(
            store,
            dispatcher,
            configureOutbox: outbox =>
            {
                outbox.Contracts.Register<OutboxTests.OrderSubmittedIntegrationEvent>("orders.events.submitted", 1);
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
                options.PollInterval = TimeSpan.FromSeconds(1);
            });

        var outbox = provider.GetRequiredService<IOutbox>();
        var hostedService = provider.GetServices<IHostedService>().Single();

        for (var i = 0; i < 4; i++)
        {
            await outbox.AddAsync(new OutboxTests.OrderSubmittedIntegrationEvent { OrderId = Guid.NewGuid() }, new OutboxOptions { Id = Guid.NewGuid() });
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var startedAt = DateTimeOffset.UtcNow;
        await hostedService.StartAsync(cts.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(500));
        await hostedService.StopAsync(CancellationToken.None);

        var elapsed = DateTimeOffset.UtcNow - startedAt;
        elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));
        dispatcher.Instance!.DispatchedMessages.Should().HaveCount(4);
    }

    private static ServiceProvider BuildProvider(
        OutboxTests.InMemoryOutboxStore store,
        OutboxTestInfrastructure.RecordingOutboxDispatcherHolder dispatcherHolder,
        Action<OutboxProcessorHostOptions>? configureHost = null,
        Action<OutboxModuleBuilder>? configureOutbox = null,
        IOutboxLeaseStore? leaseStore = null)
    {
        return new ServiceCollection()
            .AddSingleton<IOutboxStore>(store)
            .AddSingleton<IOutboxLeaseStore>(leaseStore ?? store)
            .AddSingleton<IOutboxStateStore>(store)
            .AddSingleton(dispatcherHolder)
            .AddSingleton<OutboxTestInfrastructure.RecordingOutboxDispatcher>(sp =>
            {
                var dispatcher = new OutboxTestInfrastructure.RecordingOutboxDispatcher(
                    sp.GetRequiredService<IMessageContractRegistry>(),
                    sp.GetRequiredService<IMessageSerializer>());

                dispatcherHolder.Instance = dispatcher;
                return dispatcher;
            })
            .AddSingleton<IOutboxDispatcher>(sp => sp.GetRequiredService<OutboxTestInfrastructure.RecordingOutboxDispatcher>())
            .AddLiteBus(configuration =>
            {
                configuration.AddOutboxModule(outbox =>
                {
                    if (configureOutbox is not null)
                    {
                        configureOutbox(outbox);
                    }
                    else
                    {
                        outbox.Contracts.Register<OutboxTests.OrderSubmittedIntegrationEvent>("orders.events.submitted", 1);
                        outbox.UseProcessorOptions(new OutboxProcessorOptions
                        {
                            BatchSize = 10,
                            LeaseOwner = "test-publisher",
                            Retry = new RetryOptions { UseJitter = false }
                        });
                    }

                    outbox.UseProcessorBackgroundWork(configureHost);
                });
            })
            .BuildServiceProvider();
    }
}
