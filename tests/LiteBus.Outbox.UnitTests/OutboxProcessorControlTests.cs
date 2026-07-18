using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.InMemory;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

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

         var provider = BuildProvider(             dispatcher,             options => options.PollInterval = TimeSpan.FromMilliseconds(50));
         await using (provider.ConfigureAwait(true))
         {

        var outbox = provider.GetRequiredService<IOutbox>();
        var control = provider.GetRequiredService<IOutboxProcessorControl>();

        var firstOrderId = Guid.NewGuid();

        await outbox.EnqueueAsync(OutboxWriterTestFactory.ItemWithId(
            new OutboxTests.OrderSubmittedIntegrationEvent { OrderId = firstOrderId },
            Guid.NewGuid())).ConfigureAwait(false);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await OutboxTestInfrastructure.StartLiteBusHostedServicesAsync(provider, cts.Token).ConfigureAwait(false);
        await Task.Delay(TimeSpan.FromMilliseconds(200)).ConfigureAwait(false);

        dispatcher.Instance!.DispatchedMessages
            .OfType<OutboxTests.OrderSubmittedIntegrationEvent>()
            .Should()
            .ContainSingle(submitted => submitted.OrderId == firstOrderId);

        await control.PauseAsync(CancellationToken.None).ConfigureAwait(false);
        control.State.Should().Be(ProcessorState.Paused);

        var pausedOrderId = Guid.NewGuid();

        await outbox.EnqueueAsync(OutboxWriterTestFactory.ItemWithId(
            new OutboxTests.OrderSubmittedIntegrationEvent { OrderId = pausedOrderId },
            Guid.NewGuid())).ConfigureAwait(false);

        await Task.Delay(TimeSpan.FromMilliseconds(250)).ConfigureAwait(false);

        dispatcher.Instance.DispatchedMessages
            .OfType<OutboxTests.OrderSubmittedIntegrationEvent>()
            .Should()
            .NotContain(submitted => submitted.OrderId == pausedOrderId);

        await control.ResumeAsync(CancellationToken.None).ConfigureAwait(false);
        control.State.Should().Be(ProcessorState.Running);

        await WaitUntilAsync(
            () => dispatcher.Instance!.DispatchedMessages
                .OfType<OutboxTests.OrderSubmittedIntegrationEvent>()
                .Any(submitted => submitted.OrderId == pausedOrderId),
            TimeSpan.FromSeconds(2)).ConfigureAwait(false);

        await OutboxTestInfrastructure.StopLiteBusHostedServicesAsync(provider, CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Confirms drain processes one final pass and completes the drain wait.
    /// </summary>
    [Fact]
    public async Task DrainAsync_should_process_pending_messages_and_complete()
    {
        var dispatcher = new OutboxTestInfrastructure.RecordingOutboxDispatcherHolder();

         var provider = BuildProvider(             dispatcher,             options => options.PollInterval = TimeSpan.FromMilliseconds(50));
         await using (provider.ConfigureAwait(true))
         {

        var outbox = provider.GetRequiredService<IOutbox>();
        var control = provider.GetRequiredService<IOutboxProcessorControl>();

        var orderId = Guid.NewGuid();

        await outbox.EnqueueAsync(OutboxWriterTestFactory.ItemWithId(
            new OutboxTests.OrderSubmittedIntegrationEvent { OrderId = orderId },
            Guid.NewGuid())).ConfigureAwait(false);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await OutboxTestInfrastructure.StartLiteBusHostedServicesAsync(provider, cts.Token).ConfigureAwait(false);

        var drainTask = control.DrainAsync(TimeSpan.FromSeconds(2), CancellationToken.None);
        await drainTask.ConfigureAwait(false);

        control.State.Should().Be(ProcessorState.Draining);

        dispatcher.Instance!.DispatchedMessages
            .OfType<OutboxTests.OrderSubmittedIntegrationEvent>()
            .Should()
            .ContainSingle(submitted => submitted.OrderId == orderId);

        await OutboxTestInfrastructure.StopLiteBusHostedServicesAsync(provider, CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Confirms the internal gate blocks loop entry while paused and unblocks after resume.
    /// </summary>
    [Fact]
    public async Task WaitIfPausedAsync_should_block_while_paused()
    {
        var control = new OutboxProcessorControl();

        await control.PauseAsync(CancellationToken.None).ConfigureAwait(false);

        var waitTask = control.WaitIfPausedAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(50)).ConfigureAwait(false);
        waitTask.IsCompleted.Should().BeFalse();

        await control.ResumeAsync(CancellationToken.None).ConfigureAwait(false);
        await waitTask.ConfigureAwait(false);
    }

    private static ServiceProvider BuildProvider(
        OutboxTestInfrastructure.RecordingOutboxDispatcherHolder dispatcherHolder,
        Action<OutboxProcessorHostOptions>? configureHost = null)
    {
        return new ServiceCollection()
            .AddSingleton(dispatcherHolder)
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ =>
                {
                });

                registry.AddOutbox(outbox =>
                {
                    outbox.Contracts.Register<OutboxTests.OrderSubmittedIntegrationEvent>("orders.events.submitted");

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
            await Task.Delay(50).ConfigureAwait(false);
        }
    }
}
