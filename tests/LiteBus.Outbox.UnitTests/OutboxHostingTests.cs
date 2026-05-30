using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Extensions.Microsoft.Hosting;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace LiteBus.Outbox.UnitTests;

[Collection("Sequential")]
public sealed class OutboxHostingTests : LiteBusTestBase
{
    [Fact]
    public async Task ProcessorHost_WhenDisabled_ShouldCompleteWithoutPublishing()
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
    public async Task ProcessorHost_ShouldRecordPassStateForHealthCheck()
    {
        var store = new OutboxTests.InMemoryOutboxStore();
        var dispatcher = new OutboxTestInfrastructure.RecordingOutboxDispatcherHolder();

        await using var provider = BuildProvider(
            store,
            dispatcher,
            configureHost: options => options.PollInterval = TimeSpan.FromMilliseconds(50));

        var outbox = provider.GetRequiredService<IOutbox>();
        var hostedService = provider.GetServices<IHostedService>().Single();
        var state = provider.GetRequiredService<OutboxProcessorHostState>();
        var healthCheck = new OutboxProcessorHealthCheck(state);

        var orderId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        await outbox.AddAsync(new OutboxTests.OrderSubmittedIntegrationEvent { OrderId = orderId }, new OutboxOptions { Id = messageId });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await hostedService.StartAsync(cts.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(300));
        await hostedService.StopAsync(CancellationToken.None);

        state.LastSuccessfulPassAt.Should().NotBeNull();
        state.ConsecutiveFailures.Should().Be(0);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());
        result.Status.Should().Be(HealthStatus.Healthy);
        dispatcher.Instance!.DispatchedMessages
            .OfType<OutboxTests.OrderSubmittedIntegrationEvent>()
            .Should()
            .ContainSingle(submitted => submitted.OrderId == orderId);
    }

    [Fact]
    public async Task ProcessorHost_BeforeFirstPass_HealthCheckShouldReportHealthyWithPendingMessage()
    {
        var store = new OutboxTests.InMemoryOutboxStore();
        var dispatcher = new OutboxTestInfrastructure.RecordingOutboxDispatcherHolder();

        await using var provider = BuildProvider(store, dispatcher);
        var state = provider.GetRequiredService<OutboxProcessorHostState>();
        var healthCheck = new OutboxProcessorHealthCheck(state);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Contain("has not completed a pass yet");
    }

    [Fact]
    public async Task ProcessorHost_WhenPassFails_ShouldRecordFailureAndReportUnhealthy()
    {
        var flakyLeaseStore = new OutboxTestInfrastructure.ThrowingOutboxLeaseStore();
        var dispatcher = new OutboxTestInfrastructure.RecordingOutboxDispatcherHolder();

        await using var provider = BuildProvider(
            flakyLeaseStore.Inner,
            dispatcher,
            configureHost: options => options.PollInterval = TimeSpan.FromMilliseconds(25),
            leaseStore: flakyLeaseStore);

        var hostedService = provider.GetServices<IHostedService>().Single();
        var state = provider.GetRequiredService<OutboxProcessorHostState>();
        var healthCheck = new OutboxProcessorHealthCheck(state);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await hostedService.StartAsync(cts.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(150));
        await hostedService.StopAsync(CancellationToken.None);

        state.ConsecutiveFailures.Should().BeGreaterThan(0);
        state.LastFailureMessage.Should().Contain("Simulated lease store failure");

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());
        result.Status.Should().Be(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task ProcessorHost_WhenRecoversAfterFailure_ShouldReportHealthyAgain()
    {
        var flakyLeaseStore = new OutboxTestInfrastructure.ThrowingOutboxLeaseStore(failuresBeforeSuccess: 1);
        var dispatcher = new OutboxTestInfrastructure.RecordingOutboxDispatcherHolder();

        await using var provider = BuildProvider(
            flakyLeaseStore.Inner,
            dispatcher,
            configureHost: options => options.PollInterval = TimeSpan.FromMilliseconds(25),
            leaseStore: flakyLeaseStore);

        var outbox = provider.GetRequiredService<IOutbox>();
        var hostedService = provider.GetServices<IHostedService>().Single();
        var state = provider.GetRequiredService<OutboxProcessorHostState>();
        var healthCheck = new OutboxProcessorHealthCheck(state);

        var orderId = Guid.NewGuid();
        await outbox.AddAsync(new OutboxTests.OrderSubmittedIntegrationEvent { OrderId = orderId }, new OutboxOptions { Id = Guid.NewGuid() });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await hostedService.StartAsync(cts.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(400));
        await hostedService.StopAsync(CancellationToken.None);

        state.ConsecutiveFailures.Should().Be(0);
        state.LastSuccessfulPassAt.Should().NotBeNull();

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());
        result.Status.Should().Be(HealthStatus.Healthy);
        dispatcher.Instance!.DispatchedMessages
            .OfType<OutboxTests.OrderSubmittedIntegrationEvent>()
            .Should()
            .ContainSingle(submitted => submitted.OrderId == orderId);
    }

    [Fact]
    public async Task ProcessorHost_WhenMissingDependency_ShouldThrowOnStart()
    {
        var services = new ServiceCollection()
            .AddLiteBus(configuration =>
            {
                configuration.AddOutboxModule(outbox =>
                {
                    outbox.Contracts.Register<OutboxTests.OrderSubmittedIntegrationEvent>("orders.events.submitted", 1);
                });

                configuration.AddOutboxProcessorHosting();
            });

        await using var provider = services.BuildServiceProvider();

        var act = () => provider.GetServices<IHostedService>().ToList();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*IOutboxLeaseStore*");
    }

    [Fact]
    public async Task ProcessorHost_WhenMissingDispatcher_ShouldThrowOnStart()
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
                    outbox.UseProcessorOptions(new OutboxProcessorOptions
                    {
                        BatchSize = 10,
                        LeaseOwner = "test-publisher",
                        Retry = new RetryOptions { UseJitter = false }
                    });
                });

                configuration.AddOutboxProcessorHosting();
            });

        await using var provider = services.BuildServiceProvider();

        var act = () => provider.GetServices<IHostedService>().ToList();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*IOutboxDispatcher*");
    }

    [Fact]
    public async Task ProcessorHost_ShouldRespectStartupDelay()
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
    public async Task ProcessorHost_WithAdaptivePollingAndFullBatch_ShouldPublishMultipleMessagesQuickly()
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

    [Fact]
    public void AddLiteBusOutboxProcessor_ShouldRegisterNamedHealthCheck()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new OutboxProcessorHostState());

        var act = () => services.AddHealthChecks().AddLiteBusOutboxProcessor("outbox-host");

        act.Should().NotThrow();
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
                    sp.GetRequiredService<LiteBus.Messaging.Abstractions.IMessageContractRegistry>(),
                    sp.GetRequiredService<LiteBus.Messaging.Abstractions.IMessageSerializer>());

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
                });

                configuration.AddOutboxProcessorHosting(configureHost);
            })
            .BuildServiceProvider();
    }
}
