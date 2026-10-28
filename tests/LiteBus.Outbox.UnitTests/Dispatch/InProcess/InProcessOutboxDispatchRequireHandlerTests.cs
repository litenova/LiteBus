using LiteBus.Events;
using LiteBus.Events.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Dispatch.InProcess;
using LiteBus.Outbox.Storage.InMemory;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Outbox.UnitTests.Dispatch.InProcess;

/// <summary>
///     Covers the default that stops an outbox message from being marked published when nothing handled it.
/// </summary>
/// <remarks>
///     The failure this guards against is silent and total: registering the event axis over an assembly that does not
///     hold the handlers left every message leased, dispatched and marked published without leaving the process, with
///     no exception, no dead letter, and a backlog reading of zero.
/// </remarks>
[Collection("Sequential")]
public sealed class InProcessOutboxDispatchRequireHandlerTests : LiteBusTestBase
{
    [Fact]
    public async Task A_message_with_no_handler_is_not_marked_published()
    {
        var serviceProvider = BuildProvider(configureDispatch: null);

        var outbox = serviceProvider.GetRequiredService<IOutbox>();
        var processor = serviceProvider.GetRequiredService<IOutboxProcessor>();
        var manager = serviceProvider.GetRequiredService<IOutboxManager>();

        var receipt = await outbox.EnqueueAsync(new UnhandledEvent(Guid.NewGuid())).ConfigureAwait(false);

        await processor.ProcessPendingAsync().ConfigureAwait(false);

        var stored = await manager.GetMessageAsync(receipt.Id).ConfigureAwait(false);

        stored.Should().NotBeNull();
        stored!.Status.Should().NotBe(OutboxStatus.Published);
        stored.AttemptCount.Should().Be(1);
        stored.LastError.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Turning_RequireHandler_off_restores_the_silent_no_op()
    {
        var serviceProvider = BuildProvider(dispatch => dispatch.RequireHandler = false);

        var outbox = serviceProvider.GetRequiredService<IOutbox>();
        var processor = serviceProvider.GetRequiredService<IOutboxProcessor>();
        var manager = serviceProvider.GetRequiredService<IOutboxManager>();

        var receipt = await outbox.EnqueueAsync(new UnhandledEvent(Guid.NewGuid())).ConfigureAwait(false);

        await processor.ProcessPendingAsync().ConfigureAwait(false);

        var stored = await manager.GetMessageAsync(receipt.Id).ConfigureAwait(false);

        stored.Should().NotBeNull();
        stored!.Status.Should().Be(OutboxStatus.Published);
    }

    private static ServiceProvider BuildProvider(Action<InProcessOutboxDispatchOptionsBuilder>? configureDispatch)
    {
        return new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ =>
                {
                });

                // The event type is known to the axis but nothing handles it, which is the shape of the mistake:
                // the events were scanned from the application assembly and their handlers live in another one that
                // AddEvents was never pointed at.
                registry.AddEvents(events => events.Register<UnhandledEvent>());

                registry.AddOutbox(builder =>
                {
                    builder.Contracts.Register<UnhandledEvent>("tests.events.unhandled");

                    builder.UseProcessorOptions(new OutboxProcessorOptions
                    {
                        BatchSize = 10,
                        LeaseOwner = "require-handler-test",
                        Retry = new RetryOptions { UseJitter = false }
                    });

                    builder.UseInMemoryStorage();

                    if (configureDispatch is null)
                    {
                        builder.UseInProcessDispatch();
                    }
                    else
                    {
                        builder.UseInProcessDispatch(configureDispatch);
                    }
                });
            })
            .BuildServiceProvider();
    }

    public sealed record UnhandledEvent(Guid Id) : IEvent;
}
