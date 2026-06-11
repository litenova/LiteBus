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

        await using var provider = BuildProvider(
            recorder,
            options => options.PollInterval = TimeSpan.FromMilliseconds(25));

        var scheduler = provider.GetRequiredService<IInbox>();
        var control = provider.GetRequiredService<IInboxProcessorControl>();

        var firstOrderId = Guid.NewGuid();

        await scheduler.AcceptAsync(InboxAcceptItems.From(new InboxTestFixtures.ShipOrderCommand
        {
            OrderId = firstOrderId,
            IdempotencyKey = $"ship:{firstOrderId}"
        }));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await InboxTestInfrastructure.StartLiteBusHostedServicesAsync(provider, cts.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(200));

        recorder.Commands.Should().ContainSingle(command => command.OrderId == firstOrderId);

        await control.PauseAsync(CancellationToken.None);
        control.State.Should().Be(ProcessorState.Paused);

        var pausedOrderId = Guid.NewGuid();

        await scheduler.AcceptAsync(InboxAcceptItems.From(new InboxTestFixtures.ShipOrderCommand
        {
            OrderId = pausedOrderId,
            IdempotencyKey = $"ship:{pausedOrderId}"
        }));

        await Task.Delay(TimeSpan.FromMilliseconds(250));
        recorder.Commands.Should().NotContain(command => command.OrderId == pausedOrderId);

        await control.ResumeAsync(CancellationToken.None);
        control.State.Should().Be(ProcessorState.Running);

        await WaitUntilAsync(() => recorder.Commands.Any(command => command.OrderId == pausedOrderId), TimeSpan.FromSeconds(10));
        recorder.Commands.Should().Contain(command => command.OrderId == pausedOrderId);

        await InboxTestInfrastructure.StopLiteBusHostedServicesAsync(provider, CancellationToken.None);
    }

    /// <summary>
    ///     Confirms drain processes one final pass and completes the drain wait.
    /// </summary>
    [Fact]
    public async Task DrainAsync_should_process_pending_messages_and_complete()
    {
        var recorder = new InboxTestFixtures.CommandRecorder();

        await using var provider = BuildProvider(
            recorder,
            options => options.PollInterval = TimeSpan.FromMilliseconds(50));

        var scheduler = provider.GetRequiredService<IInbox>();
        var control = provider.GetRequiredService<IInboxProcessorControl>();

        var orderId = Guid.NewGuid();

        await scheduler.AcceptAsync(InboxAcceptItems.From(new InboxTestFixtures.ShipOrderCommand
        {
            OrderId = orderId,
            IdempotencyKey = $"ship:{orderId}"
        }));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await InboxTestInfrastructure.StartLiteBusHostedServicesAsync(provider, cts.Token);

        var drainTask = control.DrainAsync(TimeSpan.FromSeconds(2), CancellationToken.None);
        await drainTask;

        control.State.Should().Be(ProcessorState.Draining);
        recorder.Commands.Should().ContainSingle(command => command.OrderId == orderId);

        await InboxTestInfrastructure.StopLiteBusHostedServicesAsync(provider, CancellationToken.None);
    }

    /// <summary>
    ///     Confirms the internal gate blocks loop entry while paused and unblocks after resume.
    /// </summary>
    [Fact]
    public async Task WaitIfPausedAsync_should_block_while_paused()
    {
        var control = new InboxProcessorControl();

        await control.PauseAsync(CancellationToken.None);

        var waitTask = control.WaitIfPausedAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(50));
        waitTask.IsCompleted.Should().BeFalse();

        await control.ResumeAsync(CancellationToken.None);
        await waitTask;
    }

    private static ServiceProvider BuildProvider(
        InboxTestFixtures.CommandRecorder recorder,
        Action<InboxProcessorHostOptions>? configureHost = null)
    {
        return new ServiceCollection()
            .AddSingleton(recorder)
            .AddLiteBus(registry =>
            {
                registry.AddMessageModule(_ =>
                {
                });

                registry.AddCommandModule(builder =>
                {
                    builder.Register<InboxTestFixtures.ShipOrderCommand>();
                    builder.Register<InboxTestFixtures.ShipOrderCommandHandler>();
                });

                registry.AddInboxModule(inbox =>
                {
                    inbox.Contracts.Register<InboxTestFixtures.ShipOrderCommand>("orders.commands.ship");

                    inbox.UseProcessorOptions(new InboxProcessorOptions
                    {
                        BatchSize = 10,
                        LeaseOwner = "test-worker",
                        Retry = new RetryOptions { UseJitter = false }
                    });

                    inbox.UseInMemoryStorage();
                    inbox.UseCommandInboxDispatcher();
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
            await Task.Delay(50);
        }
    }
}