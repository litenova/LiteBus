using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Extensions.Microsoft.Hosting;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace LiteBus.Inbox.UnitTests;

[Collection("Sequential")]
public sealed class CommandInboxHostingTests : LiteBusTestBase
{
    [Fact]
    public async Task ProcessorHost_WhenDisabled_ShouldCompleteWithoutProcessing()
    {
        var store = new CommandInboxTests.InMemoryCommandInboxStore();
        var recorder = new CommandInboxTests.CommandRecorder();

        await using var provider = BuildProvider(store, recorder, hostOptions => hostOptions.Enabled = false);
        var hostedService = provider.GetServices<IHostedService>().Single();

        await hostedService.StartAsync(CancellationToken.None);
        await hostedService.StopAsync(CancellationToken.None);

        recorder.Commands.Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessorHost_ShouldRecordPassStateForHealthCheck()
    {
        var store = new CommandInboxTests.InMemoryCommandInboxStore();
        var recorder = new CommandInboxTests.CommandRecorder();

        await using var provider = BuildProvider(
            store,
            recorder,
            configureHost: options => options.PollInterval = TimeSpan.FromMilliseconds(50));

        var scheduler = provider.GetRequiredService<ICommandScheduler>();
        var hostedService = provider.GetServices<IHostedService>().Single();
        var state = provider.GetRequiredService<CommandInboxProcessorHostState>();
        var healthCheck = new CommandInboxProcessorHealthCheck(state);

        var orderId = Guid.NewGuid();
        await scheduler.ScheduleAsync(new CommandInboxTests.ShipOrderCommand
        {
            OrderId = orderId,
            IdempotencyKey = $"ship:{orderId}"
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await hostedService.StartAsync(cts.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(300));
        await hostedService.StopAsync(CancellationToken.None);

        state.LastSuccessfulPassAt.Should().NotBeNull();
        state.ConsecutiveFailures.Should().Be(0);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());
        result.Status.Should().Be(HealthStatus.Healthy);
        recorder.Commands.Should().ContainSingle(command => command.OrderId == orderId);
    }

    [Fact]
    public async Task ProcessorHost_BeforeFirstPass_HealthCheckShouldReportHealthyWithPendingMessage()
    {
        var store = new CommandInboxTests.InMemoryCommandInboxStore();
        var recorder = new CommandInboxTests.CommandRecorder();

        await using var provider = BuildProvider(store, recorder);
        var state = provider.GetRequiredService<CommandInboxProcessorHostState>();
        var healthCheck = new CommandInboxProcessorHealthCheck(state);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Contain("has not completed a pass yet");
    }

    [Fact]
    public async Task ProcessorHost_WhenPassFails_ShouldRecordFailureAndReportUnhealthy()
    {
        var flakyLeaseStore = new InboxTestInfrastructure.ThrowingInboxLeaseStore();
        var recorder = new CommandInboxTests.CommandRecorder();

        await using var provider = BuildProvider(
            flakyLeaseStore.Inner,
            recorder,
            configureHost: options => options.PollInterval = TimeSpan.FromMilliseconds(25),
            leaseStore: flakyLeaseStore);

        var hostedService = provider.GetServices<IHostedService>().Single();
        var state = provider.GetRequiredService<CommandInboxProcessorHostState>();
        var healthCheck = new CommandInboxProcessorHealthCheck(state);

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
        var flakyLeaseStore = new InboxTestInfrastructure.ThrowingInboxLeaseStore(failuresBeforeSuccess: 1);
        var recorder = new CommandInboxTests.CommandRecorder();

        await using var provider = BuildProvider(
            flakyLeaseStore.Inner,
            recorder,
            configureHost: options => options.PollInterval = TimeSpan.FromMilliseconds(25),
            leaseStore: flakyLeaseStore);

        var scheduler = provider.GetRequiredService<ICommandScheduler>();
        var hostedService = provider.GetServices<IHostedService>().Single();
        var state = provider.GetRequiredService<CommandInboxProcessorHostState>();
        var healthCheck = new CommandInboxProcessorHealthCheck(state);

        var orderId = Guid.NewGuid();
        await scheduler.ScheduleAsync(new CommandInboxTests.ShipOrderCommand
        {
            OrderId = orderId,
            IdempotencyKey = $"ship:{orderId}"
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await hostedService.StartAsync(cts.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(400));
        await hostedService.StopAsync(CancellationToken.None);

        state.ConsecutiveFailures.Should().Be(0);
        state.LastSuccessfulPassAt.Should().NotBeNull();

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());
        result.Status.Should().Be(HealthStatus.Healthy);
        recorder.Commands.Should().ContainSingle(command => command.OrderId == orderId);
    }

    [Fact]
    public async Task ProcessorHost_WhenMissingDependency_ShouldThrowOnStart()
    {
        var services = new ServiceCollection()
            .AddLiteBus(configuration =>
            {
                configuration.AddCommandInboxModule(inbox =>
                {
                    inbox.Contracts.Register<CommandInboxTests.ShipOrderCommand>("orders.commands.ship", 1);
                });

                configuration.AddCommandInboxProcessorHosting();
            });

        await using var provider = services.BuildServiceProvider();

        var act = () => provider.GetServices<IHostedService>().ToList();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ICommandInboxLeaseStore*");
    }

    [Fact]
    public async Task ProcessorHost_ShouldRespectStartupDelay()
    {
        var store = new CommandInboxTests.InMemoryCommandInboxStore();
        var recorder = new CommandInboxTests.CommandRecorder();

        await using var provider = BuildProvider(
            store,
            recorder,
            configureHost: options =>
            {
                options.StartupDelay = TimeSpan.FromMilliseconds(300);
                options.PollInterval = TimeSpan.FromMilliseconds(50);
            });

        var scheduler = provider.GetRequiredService<ICommandScheduler>();
        var hostedService = provider.GetServices<IHostedService>().Single();

        var orderId = Guid.NewGuid();
        await scheduler.ScheduleAsync(new CommandInboxTests.ShipOrderCommand
        {
            OrderId = orderId,
            IdempotencyKey = $"ship:{orderId}"
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await hostedService.StartAsync(cts.Token);

        await Task.Delay(TimeSpan.FromMilliseconds(100));
        recorder.Commands.Should().BeEmpty();

        await Task.Delay(TimeSpan.FromMilliseconds(350));
        await hostedService.StopAsync(CancellationToken.None);

        recorder.Commands.Should().ContainSingle(command => command.OrderId == orderId);
    }

    [Fact]
    public async Task ProcessorHost_WithAdaptivePollingAndFullBatch_ShouldProcessMultipleCommandsQuickly()
    {
        var store = new CommandInboxTests.InMemoryCommandInboxStore();
        var recorder = new CommandInboxTests.CommandRecorder();

        await using var provider = BuildProvider(
            store,
            recorder,
            configureInbox: inbox =>
            {
                inbox.Contracts.Register<CommandInboxTests.ShipOrderCommand>("orders.commands.ship", 1);
                inbox.UseProcessorOptions(new CommandInboxProcessorOptions
                {
                    BatchSize = 2,
                    LeaseOwner = "test-worker",
                    Retry = new RetryOptions { UseJitter = false }
                });
            },
            configureHost: options =>
            {
                options.UseAdaptivePolling = true;
                options.PollInterval = TimeSpan.FromSeconds(1);
            });

        var scheduler = provider.GetRequiredService<ICommandScheduler>();
        var hostedService = provider.GetServices<IHostedService>().Single();

        for (var i = 0; i < 4; i++)
        {
            var orderId = Guid.NewGuid();
            await scheduler.ScheduleAsync(new CommandInboxTests.ShipOrderCommand
            {
                OrderId = orderId,
                IdempotencyKey = $"ship:{orderId}"
            });
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var startedAt = DateTimeOffset.UtcNow;
        await hostedService.StartAsync(cts.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(500));
        await hostedService.StopAsync(CancellationToken.None);

        var elapsed = DateTimeOffset.UtcNow - startedAt;
        elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));
        recorder.Commands.Should().HaveCount(4);
    }

    [Fact]
    public void AddLiteBusCommandInboxProcessor_ShouldRegisterNamedHealthCheck()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new CommandInboxProcessorHostState());

        var act = () => services.AddHealthChecks().AddLiteBusCommandInboxProcessor("inbox-host");

        act.Should().NotThrow();
    }

    [Fact]
    public async Task ProcessPendingAsync_ShouldReturnLeasedCount()
    {
        var store = new CommandInboxTests.InMemoryCommandInboxStore();
        var recorder = new CommandInboxTests.CommandRecorder();

        await using var provider = BuildProvider(store, recorder);
        var processor = provider.GetRequiredService<ICommandInboxProcessor>();
        var scheduler = provider.GetRequiredService<ICommandScheduler>();

        var emptyPass = await processor.ProcessPendingAsync();
        emptyPass.LeasedCount.Should().Be(0);

        var orderId = Guid.NewGuid();
        await scheduler.ScheduleAsync(new CommandInboxTests.ShipOrderCommand
        {
            OrderId = orderId,
            IdempotencyKey = $"ship:{orderId}"
        });

        var pass = await processor.ProcessPendingAsync();
        pass.LeasedCount.Should().Be(1);
    }

    private static ServiceProvider BuildProvider(
        CommandInboxTests.InMemoryCommandInboxStore store,
        CommandInboxTests.CommandRecorder recorder,
        Action<CommandInboxProcessorHostOptions>? configureHost = null,
        Action<CommandInboxModuleBuilder>? configureInbox = null,
        ICommandInboxLeaseStore? leaseStore = null)
    {
        return new ServiceCollection()
            .AddSingleton<ICommandInboxWriter>(store)
            .AddSingleton<ICommandInboxLeaseStore>(leaseStore ?? store)
            .AddSingleton<ICommandInboxStateStore>(store)
            .AddSingleton(recorder)
            .AddLiteBus(configuration =>
            {
                configuration.AddCommandModule(builder =>
                {
                    builder.Register<CommandInboxTests.ShipOrderCommand>();
                    builder.Register<CommandInboxTests.ShipOrderCommandHandler>();
                });

                configuration.AddCommandInboxModule(inbox =>
                {
                    if (configureInbox is not null)
                    {
                        configureInbox(inbox);
                    }
                    else
                    {
                        inbox.Contracts.Register<CommandInboxTests.ShipOrderCommand>("orders.commands.ship", 1);
                        inbox.UseProcessorOptions(new CommandInboxProcessorOptions
                        {
                            BatchSize = 10,
                            LeaseOwner = "test-worker",
                            Retry = new RetryOptions { UseJitter = false }
                        });
                    }
                });

                configuration.AddCommandInboxProcessorHosting(configureHost);
            })
            .BuildServiceProvider();
    }
}
