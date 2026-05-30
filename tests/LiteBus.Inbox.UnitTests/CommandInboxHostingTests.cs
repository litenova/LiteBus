using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LiteBus.Inbox.UnitTests;

[Collection("Sequential")]
public sealed class CommandInboxHostingTests : LiteBusTestBase
{
    [Fact]
    public async Task ProcessorBackgroundWork_WhenDisabled_ShouldCompleteWithoutProcessing()
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
    public async Task ProcessorBackgroundWork_ShouldProcessScheduledCommands()
    {
        var store = new CommandInboxTests.InMemoryCommandInboxStore();
        var recorder = new CommandInboxTests.CommandRecorder();

        await using var provider = BuildProvider(
            store,
            recorder,
            configureHost: options => options.PollInterval = TimeSpan.FromMilliseconds(50));

        var scheduler = provider.GetRequiredService<IInbox>();
        var hostedService = provider.GetServices<IHostedService>().Single();

        var orderId = Guid.NewGuid();
        await scheduler.AddAsync(new CommandInboxTests.ShipOrderCommand
        {
            OrderId = orderId,
            IdempotencyKey = $"ship:{orderId}"
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await hostedService.StartAsync(cts.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(300));
        await hostedService.StopAsync(CancellationToken.None);

        recorder.Commands.Should().ContainSingle(command => command.OrderId == orderId);
    }

    [Fact]
    public async Task ProcessorBackgroundWork_WhenMissingDependency_ShouldThrowOnResolve()
    {
        var services = new ServiceCollection()
            .AddLiteBus(configuration =>
            {
                configuration.AddInboxModule(inbox =>
                {
                    inbox.Contracts.Register<CommandInboxTests.ShipOrderCommand>("orders.commands.ship", 1);
                    inbox.UseProcessorBackgroundWork();
                });
            });

        await using var provider = services.BuildServiceProvider();

        var act = () => provider.GetServices<IHostedService>().ToList();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*IInboxLeaseStore*");
    }

    [Fact]
    public void ProcessorBackgroundWork_WhenDispatcherMissing_ShouldThrowOnResolve()
    {
        var store = new CommandInboxTests.InMemoryCommandInboxStore();

        var services = new ServiceCollection()
            .AddSingleton<IInboxStore>(store)
            .AddSingleton<IInboxLeaseStore>(store)
            .AddSingleton<IInboxStateStore>(store)
            .AddLiteBus(configuration =>
            {
                configuration.AddInboxModule(inbox =>
                {
                    inbox.Contracts.Register<CommandInboxTests.ShipOrderCommand>("orders.commands.ship", 1);
                    inbox.UseProcessorBackgroundWork();
                });
            });

        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetServices<IHostedService>().ToList();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*IInboxDispatcher*");
    }

    [Fact]
    public async Task ProcessorBackgroundWork_ShouldRespectStartupDelay()
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

        var scheduler = provider.GetRequiredService<IInbox>();
        var hostedService = provider.GetServices<IHostedService>().Single();

        var orderId = Guid.NewGuid();
        await scheduler.AddAsync(new CommandInboxTests.ShipOrderCommand
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
    public async Task ProcessorBackgroundWork_WithAdaptivePollingAndFullBatch_ShouldProcessMultipleCommandsQuickly()
    {
        var store = new CommandInboxTests.InMemoryCommandInboxStore();
        var recorder = new CommandInboxTests.CommandRecorder();

        await using var provider = BuildProvider(
            store,
            recorder,
            configureInbox: inbox =>
            {
                inbox.Contracts.Register<CommandInboxTests.ShipOrderCommand>("orders.commands.ship", 1);
                inbox.UseProcessorOptions(new InboxProcessorOptions
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

        var scheduler = provider.GetRequiredService<IInbox>();
        var hostedService = provider.GetServices<IHostedService>().Single();

        for (var i = 0; i < 4; i++)
        {
            var orderId = Guid.NewGuid();
            await scheduler.AddAsync(new CommandInboxTests.ShipOrderCommand
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
    public async Task ProcessPendingAsync_ShouldReturnLeasedCount()
    {
        var store = new CommandInboxTests.InMemoryCommandInboxStore();
        var recorder = new CommandInboxTests.CommandRecorder();

        await using var provider = BuildProvider(store, recorder);
        var processor = provider.GetRequiredService<IInboxProcessor>();
        var scheduler = provider.GetRequiredService<IInbox>();

        var emptyPass = await processor.ProcessPendingAsync();
        emptyPass.LeasedCount.Should().Be(0);

        var orderId = Guid.NewGuid();
        await scheduler.AddAsync(new CommandInboxTests.ShipOrderCommand
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
        Action<InboxProcessorHostOptions>? configureHost = null,
        Action<InboxModuleBuilder>? configureInbox = null,
        IInboxLeaseStore? leaseStore = null)
    {
        return new ServiceCollection()
            .AddSingleton<IInboxStore>(store)
            .AddSingleton<IInboxLeaseStore>(leaseStore ?? store)
            .AddSingleton<IInboxStateStore>(store)
            .AddSingleton(recorder)
            .AddCommandMediatorInboxDispatcher()
            .AddLiteBus(configuration =>
            {
                configuration.AddCommandModule(builder =>
                {
                    builder.Register<CommandInboxTests.ShipOrderCommand>();
                    builder.Register<CommandInboxTests.ShipOrderCommandHandler>();
                });

                configuration.AddInboxModule(inbox =>
                {
                    if (configureInbox is not null)
                    {
                        configureInbox(inbox);
                    }
                    else
                    {
                        inbox.Contracts.Register<CommandInboxTests.ShipOrderCommand>("orders.commands.ship", 1);
                        inbox.UseProcessorOptions(new InboxProcessorOptions
                        {
                            BatchSize = 10,
                            LeaseOwner = "test-worker",
                            Retry = new RetryOptions { UseJitter = false }
                        });
                    }

                    inbox.UseProcessorBackgroundWork(configureHost);
                });
            })
            .BuildServiceProvider();
    }
}
