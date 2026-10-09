using LiteBus.Messaging.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.InMemory;
using LiteBus.Runtime.Abstractions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Outbox.UnitTests;

[Collection("Sequential")]
public sealed class OutboxProcessorHostedServiceResolutionTests : LiteBusTestBase
{
    [Fact]
    public async Task GetOutboxProcessorHostedService_WithStartupTask_ShouldResolveAndStartProcessorLoop()
    {
        RecordingStartupTask.RunCount = 0;
        var dispatcherHolder = new OutboxTestInfrastructure.RecordingOutboxDispatcherHolder();

        var provider = new ServiceCollection()
            .AddSingleton(dispatcherHolder)
            .AddLiteBus(registry =>
            {
                registry.Modules.Register(new RecordingStartupTaskModule());

                registry.AddMessaging(_ =>
                {
                });

                registry.AddOutbox(outbox =>
                {
                    outbox.Contracts.Register<OutboxTests.OrderSubmittedIntegrationEvent>("orders.events.submitted");

                    outbox.UseProcessorOptions(new OutboxProcessorOptions
                    {
                        BatchSize = 10,
                        LeaseOwner = "test-publisher",
                        Retry = new RetryOptions { UseJitter = false }
                    });

                    outbox.UseInMemoryStorage();
                    outbox.UseRecordingOutboxDispatcher(dispatcherHolder);
                    outbox.EnableOutboxProcessor(options => options.PollInterval = TimeSpan.FromMilliseconds(50));
                });
            })
            .BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            var hostedService = OutboxTestInfrastructure.GetOutboxProcessorHostedService(provider);
            hostedService.Should().NotBeNull();

            var outbox = provider.GetRequiredService<IOutbox>();
            var orderId = Guid.NewGuid();

            await outbox.EnqueueAsync(OutboxWriterTestFactory.ItemWithId(
                new OutboxTests.OrderSubmittedIntegrationEvent { OrderId = orderId },
                Guid.NewGuid())).ConfigureAwait(false);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await OutboxTestInfrastructure.StartLiteBusHostedServicesAsync(provider, cts.Token).ConfigureAwait(false);
            try
            {
                await WaitUntilAsync(
                    () => dispatcherHolder.Instance!.DispatchedMessages.Count != 0,
                    TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            }
            finally
            {
                await OutboxTestInfrastructure.StopLiteBusHostedServicesAsync(provider, CancellationToken.None).ConfigureAwait(false);
            }

            RecordingStartupTask.RunCount.Should().Be(1);
            dispatcherHolder.Instance!.DispatchedMessages
                .OfType<OutboxTests.OrderSubmittedIntegrationEvent>()
                .Should()
                .ContainSingle(submitted => submitted.OrderId == orderId);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;

        while (!condition() && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(50).ConfigureAwait(false);
        }
    }

    public sealed class RecordingStartupTaskModule : IModule
    {
        public void Build(IModuleConfiguration configuration)
        {
            configuration.RegisterStartupTask(typeof(RecordingStartupTask));
        }
    }

    public sealed class RecordingStartupTask : IStartupTask
    {
        private static int _runCount;

        public static int RunCount
        {
            get => _runCount;
            set => _runCount = value;
        }

        public Task RunAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _runCount);
            return Task.CompletedTask;
        }
    }
}
