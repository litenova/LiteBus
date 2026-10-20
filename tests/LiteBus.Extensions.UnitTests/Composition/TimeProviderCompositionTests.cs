using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging;
using LiteBus.Outbox;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.InMemory;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Extensions.UnitTests.Composition;

/// <summary>
///     Covers that a manual clock threaded through the messaging and in-memory storage builders stamps envelopes and
///     resolves from the container.
/// </summary>
public sealed class TimeProviderCompositionTests
{
    /// <summary>
    ///     Verifies inbox and outbox writers stamp envelopes with the manual clock and the container resolves it.
    /// </summary>
    [Fact]
    public async Task ManualClock_ThroughBuilders_StampsEnvelopesAndResolvesEverywhere()
    {
        var now = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new ManualTimeProvider(now);

        using var provider = new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(messaging => messaging.UseTimeProvider(timeProvider));
                registry.AddInbox(inbox =>
                {
                    inbox.Contracts.Register<ClockProbeCommand>("test.clock.command", 1);
                    inbox.UseInMemoryStorage(storage => storage.UseTimeProvider(timeProvider));
                });
                registry.AddOutbox(outbox =>
                {
                    outbox.Contracts.Register<ClockProbeEvent>("test.clock.event", 1);
                    outbox.UseInMemoryStorage(storage => storage.UseTimeProvider(timeProvider));
                });
            })
            .BuildServiceProvider();

        provider.GetRequiredService<TimeProvider>().Should().BeSameAs(timeProvider);

        var inbox = provider.GetRequiredService<IInbox>();
        var inboxReceipt = await inbox.AcceptAsync(new ClockProbeCommand()).ConfigureAwait(false);
        inboxReceipt.AcceptedAt.Should().Be(now);
        provider.GetRequiredService<InMemoryInboxStore>().Get(inboxReceipt.Id).CreatedAt.Should().Be(now);

        var outbox = provider.GetRequiredService<IOutbox>();
        var outboxReceipt = await outbox.EnqueueAsync(new ClockProbeEvent()).ConfigureAwait(false);
        outboxReceipt.StoredAt.Should().Be(now);
        provider.GetRequiredService<InMemoryOutboxStore>().Get(outboxReceipt.Id).CreatedAt.Should().Be(now);

        timeProvider.Advance(TimeSpan.FromHours(1));

        var laterInboxReceipt = await inbox.AcceptAsync(new ClockProbeCommand()).ConfigureAwait(false);
        laterInboxReceipt.AcceptedAt.Should().Be(now.AddHours(1));
    }

    private sealed record ClockProbeCommand : ICommand;

    private sealed record ClockProbeEvent;
}
