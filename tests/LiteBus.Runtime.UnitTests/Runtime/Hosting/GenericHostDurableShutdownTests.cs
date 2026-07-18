using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Dispatch.InProcess;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LiteBus.Runtime.UnitTests.Runtime.Hosting;

/// <summary>
///     Verifies durable processor shutdown behavior through a complete Microsoft Generic Host lifecycle.
/// </summary>
public sealed class GenericHostDurableShutdownTests : LiteBusTestBase
{
    /// <summary>
    ///     Confirms host shutdown waits for an active dispatch and persists its completed terminal state.
    /// </summary>
    /// <returns>A task that completes after the host has stopped and the inbox row is completed.</returns>
    [Fact]
    public async Task StopAsync_WhenInboxDispatchIsActive_ShouldWaitForCompletionAndPersistTerminalState()
    {
        var gate = new ShutdownDispatchGate();
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton(gate);
        builder.Services.AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddCommands(commands =>
            {
                commands.Register<ShutdownCommand>();
                commands.Register<ShutdownCommandHandler>();
            });

            registry.AddInbox(inbox =>
            {
                inbox.UseInMemoryStorage();
                inbox.Contracts.Register<ShutdownCommand>("tests.commands.host-shutdown");
                inbox.UseProcessorOptions(new InboxProcessorOptions
                {
                    BatchSize = 1,
                    DispatcherConcurrency = 1,
                    LeaseOwner = "generic-host-shutdown-worker",
                    LeaseDuration = TimeSpan.FromSeconds(10),
                    LeaseHeartbeatInterval = TimeSpan.FromMilliseconds(250),
                    HonorShutdownTokenOnPersist = false,
                    Retry = new RetryOptions { UseJitter = false }
                });
                inbox.UseInProcessDispatch();
                inbox.EnableInboxProcessor(host => host.PollInterval = TimeSpan.FromMilliseconds(25));
            });
        });

        var host = builder.Build();
        using (host)
        {
            var inbox = host.Services.GetRequiredService<IInbox>();
            var receipt = await inbox.AcceptAsync(new ShutdownCommand(Guid.NewGuid())).ConfigureAwait(false);

            using var timeoutSource = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await host.StartAsync(timeoutSource.Token).ConfigureAwait(false);
            await gate.Entered.Task.WaitAsync(timeoutSource.Token).ConfigureAwait(false);

            var stopTask = host.StopAsync(timeoutSource.Token);
            var cancellationObserved = await WaitUntilAsync(
                () => gate.DispatchToken.IsCancellationRequested,
                TimeSpan.FromSeconds(2)).ConfigureAwait(false);

            cancellationObserved.Should().BeTrue("host shutdown should cancel the active dispatch token");
            stopTask.IsCompleted.Should().BeFalse("host shutdown should wait for the active handler to finish");

            gate.Release.TrySetResult();
            await stopTask.ConfigureAwait(false);

            var store = host.Services.GetRequiredService<InMemoryInboxStore>();
            var envelope = store.Get(receipt.Id);
            envelope.Status.Should().Be(InboxStatus.Completed);
            envelope.AttemptCount.Should().Be(1);
        }
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25)).ConfigureAwait(false);
        }

        return condition();
    }

    private sealed record ShutdownCommand(Guid WorkId) : ICommand;

    private sealed class ShutdownCommandHandler : ICommandHandler<ShutdownCommand>
    {
        private readonly ShutdownDispatchGate _gate;

        public ShutdownCommandHandler(ShutdownDispatchGate gate)
        {
            _gate = gate;
        }

        public async Task HandleAsync(ShutdownCommand message, CancellationToken cancellationToken = default)
        {
            _gate.DispatchToken = cancellationToken;
            _gate.Entered.TrySetResult();
            await _gate.Release.Task.ConfigureAwait(false);
        }
    }

    private sealed class ShutdownDispatchGate
    {
        public CancellationToken DispatchToken { get; set; }

        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
