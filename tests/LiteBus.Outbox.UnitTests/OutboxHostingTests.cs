using LiteBus.Events;
using LiteBus.Events.Abstractions;
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
        var recorder = new OutboxTests.EventRecorder();

        await using var provider = BuildProvider(store, recorder, hostOptions => hostOptions.Enabled = false);
        var hostedService = provider.GetServices<IHostedService>().Single();

        await hostedService.StartAsync(CancellationToken.None);
        await hostedService.StopAsync(CancellationToken.None);

        recorder.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessorHost_ShouldRecordPassStateForHealthCheck()
    {
        var store = new OutboxTests.InMemoryOutboxStore();
        var recorder = new OutboxTests.EventRecorder();

        await using var provider = BuildProvider(
            store,
            recorder,
            configureHost: options => options.PollInterval = TimeSpan.FromMilliseconds(50));

        var outbox = provider.GetRequiredService<IIntegrationOutbox>();
        var hostedService = provider.GetServices<IHostedService>().Single();
        var state = provider.GetRequiredService<OutboxProcessorHostState>();
        var healthCheck = new OutboxProcessorHealthCheck(state);

        var orderId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        await outbox.AddAsync(new OutboxTests.OrderSubmittedIntegrationEvent { OrderId = orderId }, new OutboxOptions { MessageId = messageId });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await hostedService.StartAsync(cts.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(300));
        await hostedService.StopAsync(CancellationToken.None);

        state.LastSuccessfulPassAt.Should().NotBeNull();
        state.ConsecutiveFailures.Should().Be(0);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());
        result.Status.Should().Be(HealthStatus.Healthy);
        recorder.Events.Should().ContainSingle(@event => @event.OrderId == orderId);
    }

    [Fact]
    public async Task ProcessorHost_BeforeFirstPass_HealthCheckShouldReportHealthyWithPendingMessage()
    {
        var store = new OutboxTests.InMemoryOutboxStore();
        var recorder = new OutboxTests.EventRecorder();

        await using var provider = BuildProvider(store, recorder);
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
        var recorder = new OutboxTests.EventRecorder();

        await using var provider = BuildProvider(
            flakyLeaseStore.Inner,
            recorder,
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
        var recorder = new OutboxTests.EventRecorder();

        await using var provider = BuildProvider(
            flakyLeaseStore.Inner,
            recorder,
            configureHost: options => options.PollInterval = TimeSpan.FromMilliseconds(25),
            leaseStore: flakyLeaseStore);

        var outbox = provider.GetRequiredService<IIntegrationOutbox>();
        var hostedService = provider.GetServices<IHostedService>().Single();
        var state = provider.GetRequiredService<OutboxProcessorHostState>();
        var healthCheck = new OutboxProcessorHealthCheck(state);

        var orderId = Guid.NewGuid();
        await outbox.AddAsync(new OutboxTests.OrderSubmittedIntegrationEvent { OrderId = orderId }, new OutboxOptions { MessageId = Guid.NewGuid() });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await hostedService.StartAsync(cts.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(400));
        await hostedService.StopAsync(CancellationToken.None);

        state.ConsecutiveFailures.Should().Be(0);
        state.LastSuccessfulPassAt.Should().NotBeNull();

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());
        result.Status.Should().Be(HealthStatus.Healthy);
        recorder.Events.Should().ContainSingle(@event => @event.OrderId == orderId);
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
            .WithMessage("*IOutboxMessageLeaseStore*");
    }

    [Fact]
    public async Task ProcessorHost_WhenMissingDispatcher_ShouldThrowOnStart()
    {
        var store = new OutboxTests.InMemoryOutboxStore();

        var services = new ServiceCollection()
            .AddSingleton<IOutboxMessageWriter>(store)
            .AddSingleton<IOutboxMessageLeaseStore>(store)
            .AddSingleton<IOutboxMessageStateStore>(store)
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
        var recorder = new OutboxTests.EventRecorder();

        await using var provider = BuildProvider(
            store,
            recorder,
            configureHost: options =>
            {
                options.StartupDelay = TimeSpan.FromMilliseconds(300);
                options.PollInterval = TimeSpan.FromMilliseconds(50);
            });

        var outbox = provider.GetRequiredService<IIntegrationOutbox>();
        var hostedService = provider.GetServices<IHostedService>().Single();

        var orderId = Guid.NewGuid();
        await outbox.AddAsync(new OutboxTests.OrderSubmittedIntegrationEvent { OrderId = orderId }, new OutboxOptions { MessageId = Guid.NewGuid() });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await hostedService.StartAsync(cts.Token);

        await Task.Delay(TimeSpan.FromMilliseconds(100));
        recorder.Events.Should().BeEmpty();

        await Task.Delay(TimeSpan.FromMilliseconds(350));
        await hostedService.StopAsync(CancellationToken.None);

        recorder.Events.Should().ContainSingle(@event => @event.OrderId == orderId);
    }

    [Fact]
    public async Task ProcessorHost_WithAdaptivePollingAndFullBatch_ShouldPublishMultipleMessagesQuickly()
    {
        var store = new OutboxTests.InMemoryOutboxStore();
        var recorder = new OutboxTests.EventRecorder();

        await using var provider = BuildProvider(
            store,
            recorder,
            configureOutbox: outbox =>
            {
                outbox.Contracts.Register<OutboxTests.OrderSubmittedIntegrationEvent>("orders.events.submitted", 1);
                outbox.UseLiteBusEventDispatcher();
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

        var outbox = provider.GetRequiredService<IIntegrationOutbox>();
        var hostedService = provider.GetServices<IHostedService>().Single();

        for (var i = 0; i < 4; i++)
        {
            await outbox.AddAsync(new OutboxTests.OrderSubmittedIntegrationEvent { OrderId = Guid.NewGuid() }, new OutboxOptions { MessageId = Guid.NewGuid() });
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var startedAt = DateTimeOffset.UtcNow;
        await hostedService.StartAsync(cts.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(500));
        await hostedService.StopAsync(CancellationToken.None);

        var elapsed = DateTimeOffset.UtcNow - startedAt;
        elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));
        recorder.Events.Should().HaveCount(4);
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
        OutboxTests.EventRecorder recorder,
        Action<OutboxProcessorHostOptions>? configureHost = null,
        Action<OutboxModuleBuilder>? configureOutbox = null,
        IOutboxMessageLeaseStore? leaseStore = null)
    {
        return new ServiceCollection()
            .AddSingleton<IOutboxMessageWriter>(store)
            .AddSingleton<IOutboxMessageLeaseStore>(leaseStore ?? store)
            .AddSingleton<IOutboxMessageStateStore>(store)
            .AddSingleton(recorder)
            .AddLiteBus(configuration =>
            {
                configuration.AddEventModule(builder => builder.Register<OutboxTests.OrderSubmittedEventHandler>());

                configuration.AddOutboxModule(outbox =>
                {
                    if (configureOutbox is not null)
                    {
                        configureOutbox(outbox);
                    }
                    else
                    {
                        outbox.Contracts.Register<OutboxTests.OrderSubmittedIntegrationEvent>("orders.events.submitted", 1);
                        outbox.UseLiteBusEventDispatcher();
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
