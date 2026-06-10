using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.InMemory;
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

        var firstOrderId = Guid.NewGuid();
        await outbox.EnqueueAsync(
            new OutboxTests.OrderSubmittedIntegrationEvent { OrderId = firstOrderId },
            new OutboxOptions { Id = Guid.NewGuid() });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await OutboxTestInfrastructure.StartLiteBusHostedServicesAsync(provider, cts.Token);
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

        await WaitUntilAsync(
            () => dispatcher.Instance!.DispatchedMessages
                .OfType<OutboxTests.OrderSubmittedIntegrationEvent>()
                .Any(submitted => submitted.OrderId == pausedOrderId),
            TimeSpan.FromSeconds(2));

        await OutboxTestInfrastructure.StopLiteBusHostedServicesAsync(provider, CancellationToken.None);
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

        var orderId = Guid.NewGuid();
        await outbox.EnqueueAsync(
            new OutboxTests.OrderSubmittedIntegrationEvent { OrderId = orderId },
            new OutboxOptions { Id = Guid.NewGuid() });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await OutboxTestInfrastructure.StartLiteBusHostedServicesAsync(provider, cts.Token);

        var drainTask = control.DrainAsync(TimeSpan.FromSeconds(2), CancellationToken.None);
        await drainTask;

        control.State.Should().Be(ProcessorState.Draining);
        dispatcher.Instance!.DispatchedMessages
            .OfType<OutboxTests.OrderSubmittedIntegrationEvent>()
            .Should()
            .ContainSingle(submitted => submitted.OrderId == orderId);

        await OutboxTestInfrastructure.StopLiteBusHostedServicesAsync(provider, CancellationToken.None);
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
            .AddLiteBus(registry =>
            {
                registry.AddMessageModule(_ => { });
                registry.AddOutboxModule(outbox =>
                {
                    outbox.Contracts.Register<OutboxTests.OrderSubmittedIntegrationEvent>("orders.events.submitted", 1);
                    outbox.UseProcessorOptions(new OutboxProcessorOptions
                    {
                        BatchSize = 10,
                        LeaseOwner = "test-worker",
                        Retry = new RetryOptions { UseJitter = false }
                    });
                    outbox.UseInMemoryStorage();
                    outbox.UseRecordingOutboxDispatcher(dispatcherHolder);
                    outbox.EnableOutboxProcessor(configureHost);
                });
            })
            .BuildServiceProvider();
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (!condition() && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(50);
        }
    }
}
