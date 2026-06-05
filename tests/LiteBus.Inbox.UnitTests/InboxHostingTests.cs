using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LiteBus.Inbox.UnitTests;

[Collection("Sequential")]
public sealed class InboxHostingTests : LiteBusTestBase
{
    [Fact]
    public async Task ProcessorBackgroundService_WhenDisabled_ShouldCompleteWithoutProcessing()
    {
        var store = new InMemoryInboxStore();
        var recorder = new InboxTestFixtures.CommandRecorder();

        await using var provider = BuildProvider(store, recorder, hostOptions => hostOptions.Enabled = false);
        var hostedService = provider.GetServices<IHostedService>().Single();

        await hostedService.StartAsync(CancellationToken.None);
        await hostedService.StopAsync(CancellationToken.None);

        recorder.Commands.Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessorBackgroundService_ShouldProcessScheduledCommands()
    {
        var store = new InMemoryInboxStore();
        var recorder = new InboxTestFixtures.CommandRecorder();

        await using var provider = BuildProvider(
            store,
            recorder,
            configureHost: options => options.PollInterval = TimeSpan.FromMilliseconds(50));

        var scheduler = provider.GetRequiredService<IInbox>();
        var hostedService = provider.GetServices<IHostedService>().Single();

        var orderId = Guid.NewGuid();
        await scheduler.AcceptAsync(new InboxTestFixtures.ShipOrderCommand
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
    public async Task ProcessorBackgroundService_WhenMissingDependency_ShouldThrowOnResolve()
    {
        var services = new ServiceCollection()
            .AddLiteBus(modules =>
            {
                modules.AddInboxModule(inbox =>
                {
                    inbox.Contracts.Register<InboxTestFixtures.ShipOrderCommand>("orders.commands.ship", 1);
                    inbox.EnableInboxProcessor();
                });
            });

        await using var provider = services.BuildServiceProvider();

        var act = () => provider.GetServices<IHostedService>().ToList();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*IInboxLeaseStore*");
    }

    [Fact]
    public void ProcessorBackgroundService_WhenDispatcherMissing_ShouldThrowOnResolve()
    {
        var store = new InMemoryInboxStore();

        var services = new ServiceCollection()
            .AddInboxStoreRoles(store)
            .AddLiteBus(modules =>
            {
                modules.AddInboxModule(inbox =>
                {
                    inbox.Contracts.Register<InboxTestFixtures.ShipOrderCommand>("orders.commands.ship", 1);
                    inbox.EnableInboxProcessor();
                });
            });

        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetServices<IHostedService>().ToList();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*IInboxDispatcher*");
    }

    [Fact]
    public async Task ProcessorBackgroundService_ShouldRespectStartupDelay()
    {
        var store = new InMemoryInboxStore();
        var recorder = new InboxTestFixtures.CommandRecorder();

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
        await scheduler.AcceptAsync(new InboxTestFixtures.ShipOrderCommand
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
    public async Task ProcessorBackgroundService_WithAdaptivePollingAndFullBatch_ShouldProcessMultipleCommandsQuickly()
    {
        var store = new InMemoryInboxStore();
        var recorder = new InboxTestFixtures.CommandRecorder();

        await using var provider = BuildProvider(
            store,
            recorder,
            configureInbox: inbox =>
            {
                inbox.Contracts.Register<InboxTestFixtures.ShipOrderCommand>("orders.commands.ship", 1);
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
            await scheduler.AcceptAsync(new InboxTestFixtures.ShipOrderCommand
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
        var store = new InMemoryInboxStore();
        var recorder = new InboxTestFixtures.CommandRecorder();

        await using var provider = BuildProvider(store, recorder);
        var processor = provider.GetRequiredService<IInboxProcessor>();
        var scheduler = provider.GetRequiredService<IInbox>();

        var emptyPass = await processor.ProcessPendingAsync();
        emptyPass.LeasedCount.Should().Be(0);

        var orderId = Guid.NewGuid();
        await scheduler.AcceptAsync(new InboxTestFixtures.ShipOrderCommand
        {
            OrderId = orderId,
            IdempotencyKey = $"ship:{orderId}"
        });

        var pass = await processor.ProcessPendingAsync();
        pass.LeasedCount.Should().Be(1);
    }

    private static ServiceProvider BuildProvider(
        InMemoryInboxStore store,
        InboxTestFixtures.CommandRecorder recorder,
        Action<InboxProcessorHostOptions>? configureHost = null,
        Action<InboxModuleBuilder>? configureInbox = null,
        IInboxLeaseStore? leaseStore = null)
    {
        return new ServiceCollection()
            .AddSingleton<IInboxStore>(store)
            .AddSingleton<IInboxLeaseStore>(leaseStore ?? store)
            .AddSingleton<IInboxTerminalStateStore>(store)
            .AddSingleton<IInboxRetentionStore>(store)
            .AddSingleton<IInboxDiagnosticsStore>(store)
            .AddSingleton<IInboxWorkSignal, InboxPollingWorkSignal>()
            .AddSingleton(recorder)
            .AddCommandMediatorInboxDispatcher()
            .AddLiteBus(modules =>
            {
                modules.AddCommandModule(builder =>
                {
                    builder.Register<InboxTestFixtures.ShipOrderCommand>();
                    builder.Register<InboxTestFixtures.ShipOrderCommandHandler>();
                });

                modules.AddInboxModule(inbox =>
                {
                    if (configureInbox is not null)
                    {
                        configureInbox(inbox);
                    }
                    else
                    {
                        inbox.Contracts.Register<InboxTestFixtures.ShipOrderCommand>("orders.commands.ship", 1);
                        inbox.UseProcessorOptions(new InboxProcessorOptions
                        {
                            BatchSize = 10,
                            LeaseOwner = "test-worker",
                            Retry = new RetryOptions { UseJitter = false }
                        });
                    }

                    inbox.EnableInboxProcessor(configureHost);
                });
            })
            .BuildServiceProvider();
    }
}

