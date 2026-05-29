using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Hosting;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
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
        var host = provider.GetRequiredService<ICommandInboxProcessorHost>();

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await host.RunAsync(cts.Token);

        recorder.Commands.Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessorHost_ShouldInvokeLifecycleHooks()
    {
        var store = new CommandInboxTests.InMemoryCommandInboxStore();
        var recorder = new CommandInboxTests.CommandRecorder();
        var lifecycle = new RecordingInboxLifecycle();

        await using var provider = BuildProvider(
            store,
            recorder,
            configureHost: options =>
            {
                options.PollInterval = TimeSpan.FromMilliseconds(50);
                options.StartupDelay = TimeSpan.Zero;
            },
            extraServices: services => services.AddSingleton<ICommandInboxProcessorHostLifecycle>(lifecycle));

        var host = provider.GetRequiredService<ICommandInboxProcessorHost>();

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
        await host.RunAsync(cts.Token);

        lifecycle.StartingCount.Should().Be(1);
        lifecycle.StoppingCount.Should().Be(1);
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
        var host = provider.GetRequiredService<ICommandInboxProcessorHost>();
        var state = provider.GetRequiredService<ICommandInboxProcessorHostState>();
        var healthCheck = new CommandInboxProcessorHealthCheck(state);

        var orderId = Guid.NewGuid();
        await scheduler.ScheduleAsync(new CommandInboxTests.ShipOrderCommand
        {
            OrderId = orderId,
            IdempotencyKey = $"ship:{orderId}"
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await host.RunAsync(cts.Token);

        state.LastSuccessfulPassAt.Should().NotBeNull();
        state.ConsecutiveFailures.Should().Be(0);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());
        result.Status.Should().Be(HealthStatus.Healthy);
        recorder.Commands.Should().ContainSingle(command => command.OrderId == orderId);
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
        Action<IServiceCollection>? extraServices = null)
    {
        var services = new ServiceCollection()
            .AddSingleton<ICommandInboxWriter>(store)
            .AddSingleton<ICommandInboxLeaseStore>(store)
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
                    inbox.Contracts.Register<CommandInboxTests.ShipOrderCommand>("orders.commands.ship", 1);
                    inbox.UseProcessorOptions(new CommandInboxProcessorOptions
                    {
                        BatchSize = 10,
                        LeaseOwner = "test-worker",
                        Retry = new RetryOptions { UseJitter = false }
                    });
                    inbox.UseProcessorHost(configureHost);
                });

                configuration.AddCommandInboxProcessorHosting();
            });

        extraServices?.Invoke(services);
        return services.BuildServiceProvider();
    }

    private sealed class RecordingInboxLifecycle : ICommandInboxProcessorHostLifecycle
    {
        public int StartingCount { get; private set; }

        public int StoppingCount { get; private set; }

        public Task OnHostStartingAsync(CancellationToken cancellationToken)
        {
            StartingCount++;
            return Task.CompletedTask;
        }

        public Task OnHostStoppingAsync(CancellationToken cancellationToken)
        {
            StoppingCount++;
            return Task.CompletedTask;
        }
    }
}
