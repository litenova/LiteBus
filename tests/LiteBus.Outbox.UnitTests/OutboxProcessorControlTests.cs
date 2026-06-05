using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.InMemory;
using LiteBus.Runtime.Abstractions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LiteBus.Outbox.UnitTests;

/// <summary>
///     Verifies outbox processor pause, resume, and drain control behavior.
/// </summary>
[Collection("Sequential")]
public sealed class OutboxProcessorControlTests : LiteBusTestBase
{
    /// <summary>
    ///     Confirms pause transitions state and blocks subsequent publishing until resume.
    /// </summary>
    [Fact]
    public async Task PauseAsync_should_block_processing_until_resume()
    {
        var dispatcher = new OutboxTestInfrastructure.RecordingOutboxDispatcherHolder();

        await using var provider = BuildProvider(
            dispatcher,
            configureHost: options => options.PollInterval = TimeSpan.FromMilliseconds(50));

        var outbox = provider.GetRequiredService<IOutbox>();
        var control = provider.GetRequiredService<IOutboxProcessorControl>();
        var hostedService = provider.GetServices<IHostedService>().Single();

        var firstOrderId = Guid.NewGuid();
        await outbox.EnqueueAsync(
            new OutboxTests.OrderSubmittedIntegrationEvent { OrderId = firstOrderId },
            new OutboxOptions { Id = Guid.NewGuid() });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await hostedService.StartAsync(cts.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(200));

        dispatcher.Instance!.DispatchedMessages
            .OfType<OutboxTests.OrderSubmittedIntegrationEvent>()
            .Should()
            .ContainSingle(submitted => submitted.OrderId == firstOrderId);

        await control.PauseAsync(CancellationToken.None);
        control.State.Should().Be(ProcessorState.Paused);

        var pausedOrderId = Guid.NewGuid();
        await outbox.EnqueueAsync(
            new OutboxTests.OrderSubmittedIntegrationEvent { OrderId = pausedOrderId },
            new OutboxOptions { Id = Guid.NewGuid() });

        await Task.Delay(TimeSpan.FromMilliseconds(250));
        dispatcher.Instance.DispatchedMessages
            .OfType<OutboxTests.OrderSubmittedIntegrationEvent>()
            .Should()
            .NotContain(submitted => submitted.OrderId == pausedOrderId);

        await control.ResumeAsync(CancellationToken.None);
        control.State.Should().Be(ProcessorState.Running);

        await Task.Delay(TimeSpan.FromMilliseconds(250));
        dispatcher.Instance.DispatchedMessages
            .OfType<OutboxTests.OrderSubmittedIntegrationEvent>()
            .Should()
            .Contain(submitted => submitted.OrderId == pausedOrderId);

        await hostedService.StopAsync(CancellationToken.None);
    }

    /// <summary>
    ///     Confirms drain processes one final pass and completes the drain wait.
    /// </summary>
    [Fact]
    public async Task DrainAsync_should_process_pending_messages_and_complete()
    {
        var dispatcher = new OutboxTestInfrastructure.RecordingOutboxDispatcherHolder();

        await using var provider = BuildProvider(
            dispatcher,
            configureHost: options => options.PollInterval = TimeSpan.FromMilliseconds(50));

        var outbox = provider.GetRequiredService<IOutbox>();
        var control = provider.GetRequiredService<IOutboxProcessorControl>();
        var hostedService = provider.GetServices<IHostedService>().Single();

        var orderId = Guid.NewGuid();
        await outbox.EnqueueAsync(
            new OutboxTests.OrderSubmittedIntegrationEvent { OrderId = orderId },
            new OutboxOptions { Id = Guid.NewGuid() });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await hostedService.StartAsync(cts.Token);

        var drainTask = control.DrainAsync(TimeSpan.FromSeconds(2), CancellationToken.None);
        await drainTask;

        control.State.Should().Be(ProcessorState.Draining);
        dispatcher.Instance!.DispatchedMessages
            .OfType<OutboxTests.OrderSubmittedIntegrationEvent>()
            .Should()
            .ContainSingle(submitted => submitted.OrderId == orderId);

        await hostedService.StopAsync(CancellationToken.None);
    }

    /// <summary>
    ///     Confirms the internal gate blocks loop entry while paused and unblocks after resume.
    /// </summary>
    [Fact]
    public async Task WaitIfPausedAsync_should_block_while_paused()
    {
        var control = new OutboxProcessorControl();

        await control.PauseAsync(CancellationToken.None);

        var waitTask = control.WaitIfPausedAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(50));
        waitTask.IsCompleted.Should().BeFalse();

        await control.ResumeAsync(CancellationToken.None);
        await waitTask;
    }

    private static ServiceProvider BuildProvider(
        OutboxTestInfrastructure.RecordingOutboxDispatcherHolder dispatcherHolder,
        Action<OutboxProcessorHostOptions>? configureHost = null)
    {
        return new ServiceCollection()
            .AddSingleton(dispatcherHolder)
            .AddLiteBus(modules =>
            {
                modules.AddOutboxModule(outbox =>
                {
                    outbox.Contracts.Register<OutboxTests.OrderSubmittedIntegrationEvent>("orders.events.submitted", 1);
                    outbox.UseProcessorOptions(new OutboxProcessorOptions
                    {
                        BatchSize = 10,
                        LeaseOwner = "test-worker",
                        Retry = new RetryOptions { UseJitter = false }
                    });
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
