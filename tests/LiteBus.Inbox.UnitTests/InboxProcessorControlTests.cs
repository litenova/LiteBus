using LiteBus.Commands;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Dispatch.InProcess;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Inbox.UnitTests;

/// <summary>
///     Verifies inbox processor pause, resume, and drain control behavior.
/// </summary>
[Collection("Sequential")]
public sealed class InboxProcessorControlTests : LiteBusTestBase
{
    /// <summary>
    ///     Confirms pause transitions state and blocks subsequent processing until resume.
    /// </summary>
    [Fact]
    public async Task PauseAsync_should_block_processing_until_resume()
    {
        var recorder = new InboxTestFixtures.CommandRecorder();

         var provider = BuildProvider(             recorder,             options => options.PollInterval = TimeSpan.FromMilliseconds(25));
         await using (provider.ConfigureAwait(true))
         {

        var scheduler = provider.GetRequiredService<IInbox>();
        var control = provider.GetRequiredService<IInboxProcessorControl>();

        var firstOrderId = Guid.NewGuid();

        await scheduler.AcceptAsync(new InboxTestFixtures.ShipOrderCommand {
            OrderId = firstOrderId,
            IdempotencyKey = $"ship:{firstOrderId}"
        }).ConfigureAwait(false);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await InboxTestInfrastructure.StartLiteBusHostedServicesAsync(provider, cts.Token).ConfigureAwait(false);
        await Task.Delay(TimeSpan.FromMilliseconds(200)).ConfigureAwait(false);

        recorder.Commands.Should().ContainSingle(command => command.OrderId == firstOrderId);

        await control.PauseAsync(CancellationToken.None).ConfigureAwait(false);
        control.State.Should().Be(ProcessorState.Paused);

        var pausedOrderId = Guid.NewGuid();

        await scheduler.AcceptAsync(new InboxTestFixtures.ShipOrderCommand {
            OrderId = pausedOrderId,
            IdempotencyKey = $"ship:{pausedOrderId}"
        }).ConfigureAwait(false);

        await Task.Delay(TimeSpan.FromMilliseconds(250)).ConfigureAwait(false);
        recorder.Commands.Should().NotContain(command => command.OrderId == pausedOrderId);

        await control.ResumeAsync(CancellationToken.None).ConfigureAwait(false);
        control.State.Should().Be(ProcessorState.Running);

        await WaitUntilAsync(() => recorder.Commands.Any(command => command.OrderId == pausedOrderId), TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        recorder.Commands.Should().Contain(command => command.OrderId == pausedOrderId);

        await InboxTestInfrastructure.StopLiteBusHostedServicesAsync(provider, CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Confirms drain processes one final pass and completes the drain wait.
    /// </summary>
    [Fact]
    public async Task DrainAsync_should_process_pending_messages_and_complete()
    {
        var recorder = new InboxTestFixtures.CommandRecorder();

         var provider = BuildProvider(             recorder,             options => options.PollInterval = TimeSpan.FromMilliseconds(50));
         await using (provider.ConfigureAwait(true))
         {

        var scheduler = provider.GetRequiredService<IInbox>();
        var control = provider.GetRequiredService<IInboxProcessorControl>();

        var orderId = Guid.NewGuid();

        await scheduler.AcceptAsync(new InboxTestFixtures.ShipOrderCommand {
            OrderId = orderId,
            IdempotencyKey = $"ship:{orderId}"
        }).ConfigureAwait(false);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await InboxTestInfrastructure.StartLiteBusHostedServicesAsync(provider, cts.Token).ConfigureAwait(false);

        var drainTask = control.DrainAsync(TimeSpan.FromSeconds(2), CancellationToken.None);
        await drainTask.ConfigureAwait(false);

        control.State.Should().Be(ProcessorState.Draining);
        recorder.Commands.Should().ContainSingle(command => command.OrderId == orderId);

        await InboxTestInfrastructure.StopLiteBusHostedServicesAsync(provider, CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Confirms the internal gate blocks loop entry while paused and unblocks after resume.
    /// </summary>
    [Fact]
    public async Task WaitIfPausedAsync_should_block_while_paused()
    {
        var control = new InboxProcessorControl();

        await control.PauseAsync(CancellationToken.None).ConfigureAwait(false);

        var waitTask = control.WaitIfPausedAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(50)).ConfigureAwait(false);
        waitTask.IsCompleted.Should().BeFalse();

        await control.ResumeAsync(CancellationToken.None).ConfigureAwait(false);
        await waitTask.ConfigureAwait(false);
        control.SignalPassComplete();
    }

    /// <summary>
    ///     Confirms pause does not complete until an active processing pass has finished.
    /// </summary>
    [Fact]
    public async Task PauseAsync_should_wait_for_active_pass()
    {
        var control = new InboxProcessorControl();

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
        var control = new InboxProcessorControl();

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
        var invalidControl = new InboxProcessorControl();

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

        var timedOutControl = new InboxProcessorControl();

        try
        {
            var timeout = () => timedOutControl.DrainAsync(TimeSpan.FromMilliseconds(20));
            await timeout.Should().ThrowAsync<TimeoutException>().ConfigureAwait(false);
        }
        finally
        {
            await timedOutControl.DisposeAsync().ConfigureAwait(false);
        }

        var control = new InboxProcessorControl();

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
        var recorder = new InboxTestFixtures.CommandRecorder();
        var provider = BuildProvider(recorder, options => options.PollInterval = TimeSpan.FromSeconds(30));
        await using (provider.ConfigureAwait(true))
        {
            var scheduler = provider.GetRequiredService<IInbox>();
            var control = provider.GetRequiredService<IInboxProcessorControl>();
            var orderId = Guid.NewGuid();

            await scheduler.AcceptAsync(new InboxTestFixtures.ShipOrderCommand
            {
                OrderId = orderId,
                IdempotencyKey = $"ship:{orderId}"
            }).ConfigureAwait(false);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await InboxTestInfrastructure.StartLiteBusHostedServicesAsync(provider, cts.Token).ConfigureAwait(false);
            await WaitUntilAsync(
                () => recorder.Commands.Any(command => command.OrderId == orderId),
                TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            await Task.Delay(TimeSpan.FromMilliseconds(50)).ConfigureAwait(false);

            await control.DrainAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            await InboxTestInfrastructure.StopLiteBusHostedServicesAsync(provider, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static ServiceProvider BuildProvider(
        InboxTestFixtures.CommandRecorder recorder,
        Action<InboxProcessorHostOptions>? configureHost = null)
    {
        return new ServiceCollection()
            .AddSingleton(recorder)
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ =>
                {
                });

                registry.AddCommands(builder =>
                {
                    builder.Register<InboxTestFixtures.ShipOrderCommand>();
                    builder.Register<InboxTestFixtures.ShipOrderCommandHandler>();
                });

                registry.AddInbox(inbox =>
                {
                    inbox.Contracts.Register<InboxTestFixtures.ShipOrderCommand>("orders.commands.ship");

                    inbox.UseProcessorOptions(new InboxProcessorOptions
                    {
                        BatchSize = 10,
                        LeaseOwner = "test-worker",
                        Retry = new RetryOptions { UseJitter = false }
                    });

                    inbox.UseInMemoryStorage();
                    inbox.UseInProcessDispatch();
                    inbox.EnableInboxProcessor(configureHost);
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
