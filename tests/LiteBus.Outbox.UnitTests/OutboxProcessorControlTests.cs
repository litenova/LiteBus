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
        control.SignalPassComplete();
    }

    /// <summary>
    ///     Confirms pause does not complete until an active publishing pass has finished.
    /// </summary>
    [Fact]
    public async Task PauseAsync_should_wait_for_active_pass()
    {
        var control = new OutboxProcessorControl();

        try
        {
            await control.WaitIfPausedAsync(CancellationToken.None).ConfigureAwait(false);
            var pauseTask = control.PauseAsync();

            await Task.Delay(TimeSpan.FromMilliseconds(20)).ConfigureAwait(false);
            pauseTask.IsCompleted.Should().BeFalse();

            control.SignalPassComplete();
            await pauseTask.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            control.State.Should().Be(ProcessorState.Paused);
        }
        finally
        {
            await control.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Confirms repeated and concurrent pause and resume requests do not deadlock or over-release the gate.
    /// </summary>
    [Fact]
    public async Task PauseAndResumeAsync_should_be_idempotent()
    {
        var control = new OutboxProcessorControl();

        try
        {
            await control.PauseAsync().ConfigureAwait(false);
            await control.PauseAsync().WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            await Task.WhenAll(control.ResumeAsync(), control.ResumeAsync()).ConfigureAwait(false);

            control.State.Should().Be(ProcessorState.Running);
        }
        finally
        {
            await control.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Confirms drain timeout and concurrent waiter behavior report the actual final-pass outcome.
    /// </summary>
    [Fact]
    public async Task DrainAsync_should_timeout_and_share_completion_across_callers()
    {
        var invalidControl = new OutboxProcessorControl();

        try
        {
            var invalidTimeout = () => invalidControl.DrainAsync(TimeSpan.FromTicks(-1));
            await invalidTimeout.Should().ThrowAsync<ArgumentOutOfRangeException>().ConfigureAwait(false);
            invalidControl.State.Should().Be(ProcessorState.Running);
        }
        finally
        {
            await invalidControl.DisposeAsync().ConfigureAwait(false);
        }

        var timedOutControl = new OutboxProcessorControl();

        try
        {
            var timeout = () => timedOutControl.DrainAsync(TimeSpan.FromMilliseconds(20));
            await timeout.Should().ThrowAsync<TimeoutException>().ConfigureAwait(false);
        }
        finally
        {
            await timedOutControl.DisposeAsync().ConfigureAwait(false);
        }

        var control = new OutboxProcessorControl();

        try
        {
            var firstDrain = control.DrainAsync(TimeSpan.FromSeconds(1));
            var secondDrain = control.DrainAsync(TimeSpan.FromSeconds(1));

            control.SignalDrainComplete();
            await Task.WhenAll(firstDrain, secondDrain).ConfigureAwait(false);
        }
        finally
        {
            await control.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Confirms drain interrupts a long polling delay before running the final pass.
    /// </summary>
    [Fact]
    public async Task DrainAsync_should_interrupt_polling_delay()
    {
        var dispatcher = new OutboxTestInfrastructure.RecordingOutboxDispatcherHolder();
        var provider = BuildProvider(dispatcher, options => options.PollInterval = TimeSpan.FromSeconds(30));
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
            await WaitUntilAsync(
                () => dispatcher.Instance?.DispatchedMessages
                    .OfType<OutboxTests.OrderSubmittedIntegrationEvent>()
                    .Any(@event => @event.OrderId == orderId) == true,
                TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            await Task.Delay(TimeSpan.FromMilliseconds(50)).ConfigureAwait(false);

            await control.DrainAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            await OutboxTestInfrastructure.StopLiteBusHostedServicesAsync(provider, CancellationToken.None).ConfigureAwait(false);
        }
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
