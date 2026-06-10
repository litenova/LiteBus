using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Dispatch.InProcess;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;
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
        var recorder = new InboxTestFixtures.CommandRecorder();

        await using var provider = BuildProvider(recorder, hostOptions => hostOptions.Enabled = false);
        await InboxTestInfrastructure.StartLiteBusHostedServicesAsync(provider, CancellationToken.None);
        await InboxTestInfrastructure.StopLiteBusHostedServicesAsync(provider, CancellationToken.None);

        recorder.Commands.Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessorBackgroundService_ShouldProcessScheduledCommands()
    {
        var recorder = new InboxTestFixtures.CommandRecorder();

        await using var provider = BuildProvider(
            recorder,
            configureHost: options => options.PollInterval = TimeSpan.FromMilliseconds(50));

        var scheduler = provider.GetRequiredService<IInbox>();
        var orderId = Guid.NewGuid();
        await scheduler.AcceptAsync(new InboxTestFixtures.ShipOrderCommand
        {
            OrderId = orderId,
            IdempotencyKey = $"ship:{orderId}"
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await InboxTestInfrastructure.StartLiteBusHostedServicesAsync(provider, cts.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(300));
        await InboxTestInfrastructure.StopLiteBusHostedServicesAsync(provider, CancellationToken.None);

        recorder.Commands.Should().ContainSingle(command => command.OrderId == orderId);
    }

    [Fact]
    public void ProcessorBackgroundService_WhenMissingDependency_ShouldThrowOnBuild()
    {
        var act = () =>
            new ServiceCollection()
                .AddLiteBus(registry =>
                {
                    registry.AddMessageModule(_ => { });
                registry.AddInboxModule(inbox =>
                    {
                        inbox.Contracts.Register<InboxTestFixtures.ShipOrderCommand>("orders.commands.ship", 1);
                        inbox.EnableInboxProcessor();
                    });
                })
                .BuildServiceProvider();

        act.Should().Throw<LiteBusConfigurationException>()
            .WithMessage("*Inbox storage is required*");
    }

    [Fact]
    public void ProcessorBackgroundService_WhenDispatcherMissing_ShouldThrowOnBuild()
    {
        var act = () =>
            new ServiceCollection()
                .AddLiteBus(registry =>
                {
                    registry.AddMessageModule(_ => { });
                    registry.AddInboxModule(inbox =>
                    {
                        inbox.Contracts.Register<InboxTestFixtures.ShipOrderCommand>("orders.commands.ship", 1);
                        inbox.UseInMemoryStorage();
                        inbox.EnableInboxProcessor();
                    });
                })
                .BuildServiceProvider();

        act.Should().Throw<LiteBusConfigurationException>()
            .WithMessage("*EnableInboxProcessor requires an inbox dispatcher*");
    }

    [Fact]
    public async Task ProcessorBackgroundService_ShouldRespectStartupDelay()
    {
        var recorder = new InboxTestFixtures.CommandRecorder();

        await using var provider = BuildProvider(
            recorder,
            configureHost: options =>
            {
                options.StartupDelay = TimeSpan.FromMilliseconds(300);
                options.PollInterval = TimeSpan.FromMilliseconds(50);
            });

        var scheduler = provider.GetRequiredService<IInbox>();

        var orderId = Guid.NewGuid();
        await scheduler.AcceptAsync(new InboxTestFixtures.ShipOrderCommand
        {
            OrderId = orderId,
            IdempotencyKey = $"ship:{orderId}"
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await InboxTestInfrastructure.StartLiteBusHostedServicesAsync(provider, cts.Token);

        await Task.Delay(TimeSpan.FromMilliseconds(100));
        recorder.Commands.Should().BeEmpty();

        await Task.Delay(TimeSpan.FromMilliseconds(350));
        await InboxTestInfrastructure.StopLiteBusHostedServicesAsync(provider, CancellationToken.None);

        recorder.Commands.Should().ContainSingle(command => command.OrderId == orderId);
    }

    [Fact]
    public async Task ProcessorBackgroundService_WithAdaptivePollingAndFullBatch_ShouldProcessMultipleCommandsQuickly()
    {
        var recorder = new InboxTestFixtures.CommandRecorder();

        await using var provider = BuildProvider(
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
                options.PollInterval = TimeSpan.FromMilliseconds(50);
            });

        var scheduler = provider.GetRequiredService<IInbox>();

        for (var i = 0; i < 4; i++)
        {
            var orderId = Guid.NewGuid();
            await scheduler.AcceptAsync(new InboxTestFixtures.ShipOrderCommand
            {
                OrderId = orderId,
                IdempotencyKey = $"ship:{orderId}"
            });
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await InboxTestInfrastructure.StartLiteBusHostedServicesAsync(provider, cts.Token);
        await WaitUntilAsync(() => recorder.Commands.Count == 4, TimeSpan.FromSeconds(10));
        await InboxTestInfrastructure.StopLiteBusHostedServicesAsync(provider, CancellationToken.None);

        recorder.Commands.Should().HaveCount(4);
    }

    [Fact]
    public async Task ProcessPendingAsync_ShouldReturnLeasedCount()
    {
        var recorder = new InboxTestFixtures.CommandRecorder();

        await using var provider = BuildProvider(recorder);
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

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (!condition() && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(50);
        }
    }

    private static ServiceProvider BuildProvider(
        InboxTestFixtures.CommandRecorder recorder,
        Action<InboxProcessorHostOptions>? configureHost = null,
        Action<InboxModuleBuilder>? configureInbox = null)
    {
        return new ServiceCollection()
            .AddSingleton(recorder)
            .AddLiteBus(registry =>
            {
                registry.AddMessageModule(_ => { });
                registry.AddCommandModule(builder =>
                {
                    builder.Register<InboxTestFixtures.ShipOrderCommand>();
                    builder.Register<InboxTestFixtures.ShipOrderCommandHandler>();
                });

                registry.AddInboxModule(inbox =>
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

                    inbox.UseInMemoryStorage();
                    inbox.UseCommandInboxDispatcher();
                    inbox.EnableInboxProcessor(configureHost);
                });
            })
            .BuildServiceProvider();
    }
}

