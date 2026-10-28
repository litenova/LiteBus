using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.InMemory;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Outbox.UnitTests;

/// <summary>
///     Verifies that an application-supplied <see cref="RetryOptions.IsPermanentFailure" /> classifier dead-letters an
///     outbox message on the first matching failure.
/// </summary>
[Collection("Sequential")]
public sealed class OutboxPermanentFailureTests : LiteBusTestBase
{
    [Fact]
    public async Task ProcessPendingAsync_WhenClassifierMarksFailurePermanent_ShouldDeadLetterOnFirstAttempt()
    {
        var serviceProvider = CreateServiceProvider(exception => exception is UseCaseUnprocessableException);
        var store = serviceProvider.GetRequiredService<InMemoryOutboxStore>();
        var outbox = serviceProvider.GetRequiredService<IOutbox>();
        var processor = serviceProvider.GetRequiredService<IOutboxProcessor>();

        var messageId = Guid.NewGuid();

        await outbox.EnqueueAsync(OutboxWriterTestFactory.ItemWithId(
            new OrderSubmittedEvent
            {
                OrderId = Guid.NewGuid()
            },
            messageId)).ConfigureAwait(false);

        await processor.ProcessPendingAsync().ConfigureAwait(false);

        var envelope = store.Get(messageId);
        envelope.Status.Should().Be(OutboxStatus.DeadLettered);
        envelope.AttemptCount.Should().Be(1);
        envelope.LastError.Should().Contain(nameof(UseCaseUnprocessableException));
        envelope.LastError.Should().Contain("Order cannot be processed.");
    }

    [Fact]
    public async Task ProcessPendingAsync_WhenNoClassifierConfigured_ShouldScheduleRetry()
    {
        var serviceProvider = CreateServiceProvider(null);
        var store = serviceProvider.GetRequiredService<InMemoryOutboxStore>();
        var outbox = serviceProvider.GetRequiredService<IOutbox>();
        var processor = serviceProvider.GetRequiredService<IOutboxProcessor>();

        var messageId = Guid.NewGuid();

        await outbox.EnqueueAsync(OutboxWriterTestFactory.ItemWithId(
            new OrderSubmittedEvent
            {
                OrderId = Guid.NewGuid()
            },
            messageId)).ConfigureAwait(false);

        await processor.ProcessPendingAsync().ConfigureAwait(false);

        var envelope = store.Get(messageId);
        envelope.Status.Should().Be(OutboxStatus.Failed);
        envelope.AttemptCount.Should().Be(1);
        envelope.LastError.Should().Contain(nameof(UseCaseUnprocessableException));
    }

    [Fact]
    public async Task ProcessPendingAsync_WhenClassifierThrows_ShouldScheduleRetry()
    {
        var serviceProvider = CreateServiceProvider(_ => throw new InvalidOperationException("Simulated classifier failure."));
        var store = serviceProvider.GetRequiredService<InMemoryOutboxStore>();
        var outbox = serviceProvider.GetRequiredService<IOutbox>();
        var processor = serviceProvider.GetRequiredService<IOutboxProcessor>();

        var messageId = Guid.NewGuid();

        await outbox.EnqueueAsync(OutboxWriterTestFactory.ItemWithId(
            new OrderSubmittedEvent
            {
                OrderId = Guid.NewGuid()
            },
            messageId)).ConfigureAwait(false);

        await processor.ProcessPendingAsync().ConfigureAwait(false);

        var envelope = store.Get(messageId);
        envelope.Status.Should().Be(OutboxStatus.Failed);
        envelope.AttemptCount.Should().Be(1);
        envelope.LastError.Should().Contain(nameof(UseCaseUnprocessableException));
    }

    private static ServiceProvider CreateServiceProvider(Func<Exception, bool>? isPermanentFailure)
    {
        return new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ =>
                {
                });

                registry.AddOutbox(builder =>
                {
                    builder.Contracts.Register<OrderSubmittedEvent>("orders.events.submitted");

                    builder.UseProcessorOptions(new OutboxProcessorOptions
                    {
                        BatchSize = 10,
                        LeaseOwner = "permanent-failure-test-publisher",
                        Retry = new RetryOptions
                        {
                            MaxAttempts = 5,
                            InitialDelay = TimeSpan.Zero,
                            UseJitter = false,
                            IsPermanentFailure = isPermanentFailure
                        }
                    });

                    builder.UseInMemoryStorage();
                    builder.UseFixedOutboxDispatcher(new UnprocessableOutboxDispatcher());
                });
            })
            .BuildServiceProvider();
    }

    private sealed record OrderSubmittedEvent
    {
        public Guid OrderId { get; init; }
    }

    private sealed class UseCaseUnprocessableException : Exception
    {
        public UseCaseUnprocessableException(string message)
            : base(message)
        {
        }
    }

    private sealed class UnprocessableOutboxDispatcher : IOutboxDispatcher
    {
        public Task DispatchAsync(OutboxEnvelope envelope, CancellationToken cancellationToken = default)
        {
            throw new UseCaseUnprocessableException("Order cannot be processed.");
        }
    }
}
