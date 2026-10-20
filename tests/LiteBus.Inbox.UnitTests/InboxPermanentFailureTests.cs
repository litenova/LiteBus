using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
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
///     Verifies that an application-supplied <see cref="RetryOptions.IsPermanentFailure" /> classifier dead-letters an
///     inbox message on the first matching failure.
/// </summary>
[Collection("Sequential")]
public sealed class InboxPermanentFailureTests : LiteBusTestBase
{
    [Fact]
    public async Task ProcessPendingAsync_WhenClassifierMarksFailurePermanent_ShouldDeadLetterOnFirstAttempt()
    {
        var serviceProvider = CreateServiceProvider(exception => exception is UseCaseUnprocessableException);
        var store = serviceProvider.GetRequiredService<InMemoryInboxStore>();
        var scheduler = serviceProvider.GetRequiredService<IInbox>();
        var processor = serviceProvider.GetRequiredService<IInboxProcessor>();

        var receipt = await scheduler.AcceptAsync(new UnprocessableCommand()).ConfigureAwait(false);

        await processor.ProcessPendingAsync().ConfigureAwait(false);

        var envelope = store.Get(receipt.Id);
        envelope.Status.Should().Be(InboxStatus.DeadLettered);
        envelope.AttemptCount.Should().Be(1);
        envelope.LastError.Should().Contain(nameof(UseCaseUnprocessableException));
        envelope.LastError.Should().Contain("Order cannot be processed.");
    }

    [Fact]
    public async Task ProcessPendingAsync_WhenNoClassifierConfigured_ShouldScheduleRetry()
    {
        var serviceProvider = CreateServiceProvider(null);
        var store = serviceProvider.GetRequiredService<InMemoryInboxStore>();
        var scheduler = serviceProvider.GetRequiredService<IInbox>();
        var processor = serviceProvider.GetRequiredService<IInboxProcessor>();

        var receipt = await scheduler.AcceptAsync(new UnprocessableCommand()).ConfigureAwait(false);

        await processor.ProcessPendingAsync().ConfigureAwait(false);

        var envelope = store.Get(receipt.Id);
        envelope.Status.Should().Be(InboxStatus.Failed);
        envelope.AttemptCount.Should().Be(1);
        envelope.LastError.Should().Contain(nameof(UseCaseUnprocessableException));
    }

    [Fact]
    public async Task ProcessPendingAsync_WhenClassifierThrows_ShouldScheduleRetry()
    {
        var serviceProvider = CreateServiceProvider(_ => throw new InvalidOperationException("Simulated classifier failure."));
        var store = serviceProvider.GetRequiredService<InMemoryInboxStore>();
        var scheduler = serviceProvider.GetRequiredService<IInbox>();
        var processor = serviceProvider.GetRequiredService<IInboxProcessor>();

        var receipt = await scheduler.AcceptAsync(new UnprocessableCommand()).ConfigureAwait(false);

        await processor.ProcessPendingAsync().ConfigureAwait(false);

        var envelope = store.Get(receipt.Id);
        envelope.Status.Should().Be(InboxStatus.Failed);
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

                registry.AddCommands(builder =>
                {
                    builder.Register<UnprocessableCommand>();
                    builder.Register<UnprocessableCommandHandler>();
                });

                registry.AddInbox(builder =>
                {
                    builder.Contracts.Register<UnprocessableCommand>("orders.commands.unprocessable");

                    builder.UseProcessorOptions(new InboxProcessorOptions
                    {
                        BatchSize = 10,
                        LeaseOwner = "permanent-failure-test-worker",
                        Retry = new RetryOptions
                        {
                            MaxAttempts = 5,
                            InitialDelay = TimeSpan.Zero,
                            UseJitter = false,
                            IsPermanentFailure = isPermanentFailure
                        }
                    });

                    builder.UseInMemoryStorage();
                    builder.UseInProcessDispatch();
                });
            })
            .BuildServiceProvider();
    }

    private sealed record UnprocessableCommand : ICommand;

    private sealed class UnprocessableCommandHandler : ICommandHandler<UnprocessableCommand>
    {
        public Task HandleAsync(UnprocessableCommand message, CancellationToken cancellationToken = default)
        {
            throw new UseCaseUnprocessableException("Order cannot be processed.");
        }
    }

    private sealed class UseCaseUnprocessableException : Exception
    {
        public UseCaseUnprocessableException(string message)
            : base(message)
        {
        }
    }
}
