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
        Action<CommandInboxProcessorHostOptions>? configureHost = null)
    {
        return new ServiceCollection()
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
                });

                configuration.AddCommandInboxProcessorHosting(configureHost);
            })
            .BuildServiceProvider();
    }
}
