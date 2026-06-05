using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.InMemory;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LiteBus.Outbox.UnitTests;

[Collection("Sequential")]
public sealed class OutboxHostingTests : LiteBusTestBase
{
    [Fact]
    public async Task ProcessorBackgroundService_WhenDisabled_ShouldCompleteWithoutPublishing()
    {
        var dispatcher = new OutboxTestInfrastructure.RecordingOutboxDispatcherHolder();

        await using var provider = BuildProvider(dispatcher, hostOptions => hostOptions.Enabled = false);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await OutboxTestInfrastructure.StartLiteBusHostedServicesAsync(provider, cts.Token);
        await OutboxTestInfrastructure.StopLiteBusHostedServicesAsync(provider, CancellationToken.None);

        dispatcher.Instance!.DispatchedMessages.Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessorBackgroundService_ShouldPublishScheduledMessages()
    {
        var dispatcher = new OutboxTestInfrastructure.RecordingOutboxDispatcherHolder();

        await using var provider = BuildProvider(
            dispatcher,
            configureHost: options => options.PollInterval = TimeSpan.FromMilliseconds(50));

        var outbox = provider.GetRequiredService<IOutbox>();

        var orderId = Guid.NewGuid();
        await outbox.EnqueueAsync(new OutboxTests.OrderSubmittedIntegrationEvent { OrderId = orderId }, new OutboxOptions { Id = Guid.NewGuid() });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await OutboxTestInfrastructure.StartLiteBusHostedServicesAsync(provider, cts.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(300));
        await OutboxTestInfrastructure.StopLiteBusHostedServicesAsync(provider, CancellationToken.None);

        dispatcher.Instance!.DispatchedMessages
            .OfType<OutboxTests.OrderSubmittedIntegrationEvent>()
            .Should()
            .ContainSingle(submitted => submitted.OrderId == orderId);
    }

    [Fact]
    public void ProcessorBackgroundService_WhenMissingDependency_ShouldThrowOnBuild()
    {
        var act = () =>
            new ServiceCollection()
                .AddLiteBus(modules =>
                {
                    modules.AddOutboxModule(outbox =>
                    {
                        outbox.Contracts.Register<OutboxTests.OrderSubmittedIntegrationEvent>("orders.events.submitted", 1);
                        outbox.EnableOutboxProcessor();
                    });
                })
                .BuildServiceProvider();

        act.Should().Throw<LiteBusConfigurationException>()
            .WithMessage("*EnableOutboxProcessor*storage*dispatcher*");
    }

    [Fact]
    public void ProcessorBackgroundService_WhenMissingDispatcher_ShouldThrowOnBuild()
    {
        var act = () =>
            new ServiceCollection()
                .AddOutboxStoreRoles(new InMemoryOutboxStore())
                .AddLiteBus(modules =>
                {
                    modules.AddOutboxModule(outbox =>
                    {
                        outbox.Contracts.Register<OutboxTests.OrderSubmittedIntegrationEvent>("orders.events.submitted", 1);
                        outbox.EnableOutboxProcessor();
                    });
                })
                .BuildServiceProvider();

        act.Should().Throw<LiteBusConfigurationException>()
            .WithMessage("*EnableOutboxProcessor*storage*dispatcher*");
    }

    [Fact]
    public async Task ProcessorBackgroundService_ShouldRespectStartupDelay()
    {
        var dispatcher = new OutboxTestInfrastructure.RecordingOutboxDispatcherHolder();

        await using var provider = BuildProvider(
            dispatcher,
            configureHost: options =>
            {
                options.StartupDelay = TimeSpan.FromMilliseconds(300);
                options.PollInterval = TimeSpan.FromMilliseconds(50);
            });

        var outbox = provider.GetRequiredService<IOutbox>();

        var orderId = Guid.NewGuid();
        await outbox.EnqueueAsync(new OutboxTests.OrderSubmittedIntegrationEvent { OrderId = orderId }, new OutboxOptions { Id = Guid.NewGuid() });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await OutboxTestInfrastructure.StartLiteBusHostedServicesAsync(provider, cts.Token);

        await Task.Delay(TimeSpan.FromMilliseconds(100));
        dispatcher.Instance!.DispatchedMessages.Should().BeEmpty();

        await Task.Delay(TimeSpan.FromMilliseconds(350));
        await OutboxTestInfrastructure.StopLiteBusHostedServicesAsync(provider, CancellationToken.None);

        dispatcher.Instance.DispatchedMessages
            .OfType<OutboxTests.OrderSubmittedIntegrationEvent>()
            .Should()
            .ContainSingle(submitted => submitted.OrderId == orderId);
    }

    [Fact]
    public async Task ProcessorBackgroundService_WithAdaptivePollingAndFullBatch_ShouldPublishMultipleMessagesQuickly()
    {
        var dispatcher = new OutboxTestInfrastructure.RecordingOutboxDispatcherHolder();

        await using var provider = BuildProvider(
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

        for (var i = 0; i < 4; i++)
        {
            await outbox.EnqueueAsync(new OutboxTests.OrderSubmittedIntegrationEvent { OrderId = Guid.NewGuid() }, new OutboxOptions { Id = Guid.NewGuid() });
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var startedAt = DateTimeOffset.UtcNow;
        await OutboxTestInfrastructure.StartLiteBusHostedServicesAsync(provider, cts.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(500));
        await OutboxTestInfrastructure.StopLiteBusHostedServicesAsync(provider, CancellationToken.None);

        var elapsed = DateTimeOffset.UtcNow - startedAt;
        elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));
        dispatcher.Instance!.DispatchedMessages.Should().HaveCount(4);
    }

    private static ServiceProvider BuildProvider(
        OutboxTestInfrastructure.RecordingOutboxDispatcherHolder dispatcherHolder,
        Action<OutboxProcessorHostOptions>? configureHost = null,
        Action<OutboxModuleBuilder>? configureOutbox = null)
    {
        return new ServiceCollection()
            .AddSingleton(dispatcherHolder)
            .AddLiteBus(modules =>
            {
                modules.AddOutboxModule(outbox =>
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

                    outbox.UseInMemoryStorage();
                    outbox.RegisterDispatcher(new RecordingOutboxDispatchModule(dispatcherHolder));
                    outbox.EnableOutboxProcessor(configureHost);
                });
            })
            .BuildServiceProvider();
    }

    /// <summary>
    ///     Registers the test recording dispatcher as an outbox child module.
    /// </summary>
    private sealed class RecordingOutboxDispatchModule : IModule
    {
        /// <summary>
        ///     Captures the dispatcher instance resolved during tests.
        /// </summary>
        private readonly OutboxTestInfrastructure.RecordingOutboxDispatcherHolder _dispatcherHolder;

        /// <summary>
        ///     Initializes a new instance of the <see cref="RecordingOutboxDispatchModule" /> class.
        /// </summary>
        /// <param name="dispatcherHolder">The holder that receives the resolved dispatcher instance.</param>
        public RecordingOutboxDispatchModule(OutboxTestInfrastructure.RecordingOutboxDispatcherHolder dispatcherHolder)
        {
            _dispatcherHolder = dispatcherHolder;
        }

        /// <inheritdoc />
        public void Build(IModuleConfiguration configuration)
        {
            configuration.DependencyRegistry.Register(new DependencyDescriptor(
                typeof(IOutboxDispatcher),
                serviceProvider =>
                {
                    var dispatcher = new OutboxTestInfrastructure.RecordingOutboxDispatcher(
                        serviceProvider.GetRequiredService<IMessageContractRegistry>(),
                        serviceProvider.GetRequiredService<IMessageSerializer>());

                    _dispatcherHolder.Instance = dispatcher;
                    return dispatcher;
                },
                InstanceLifetime.Singleton));
        }
    }
}
