using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Dispatch.InProcess;
using LiteBus.Messaging.Abstractions.DurableMessaging;
using LiteBus.Outbox;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Dispatch.InProcess;
using LiteBus.Runtime.Abstractions.Hosting;
using LiteBus.Saga.Abstractions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using IInboxProcessor = LiteBus.Inbox.Abstractions.IInboxProcessor;

namespace LiteBus.Runtime.UnitTests.Runtime.Composition;

/// <summary>
///     Smoke tests for a representative inbox, outbox, and saga composition.
/// </summary>
public sealed class LiteBusV6CompositionSmokeTests : LiteBusTestBase
{
    /// <summary>
    ///     The saga contract name registered by the smoke composition.
    /// </summary>
    private const string OrderSagaContractName = "orders.saga.advance";

    /// <summary>
    ///     Verifies that the smoke composition registers inbox, outbox, dispatchers, saga services, and hosted processors.
    /// </summary>
    [Fact]
    public void AddV6CompositionSmoke_ShouldRegisterCoreServicesAndHostedProcessors()
    {
        var services = new ServiceCollection();
        services.AddV6CompositionSmoke();

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IInbox>().Should().NotBeNull();
        provider.GetRequiredService<IOutbox>().Should().NotBeNull();
        provider.GetRequiredService<IInboxDispatcher>().Should().BeOfType<CommandInboxDispatcher>();
        provider.GetRequiredService<IOutboxDispatcher>().Should().BeOfType<EventOutboxDispatcher>();
        provider.GetRequiredService<ISagaStore>().Should().NotBeNull();
        provider.GetRequiredService<ISagaContext>().Should().NotBeNull();

        provider.GetServices<IHostedService>().Should().ContainSingle()
            .Which.GetType().Name.Should().Be("LiteBusHostOrchestrator");

        LiteBusHostedServiceExtensions.AssertBackgroundServices(
            provider,
            typeof(InboxProcessorBackgroundService),
            typeof(OutboxProcessorBackgroundService));
    }

    /// <summary>
    ///     Verifies that two correlated inbox commands advance saga state through the smoke composition.
    /// </summary>
    [Fact]
    public async Task AddV6CompositionSmoke_ShouldPersistSagaStateAcrossCorrelatedCommands()
    {
        var services = new ServiceCollection();
        services.AddV6CompositionSmoke();

        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            var inbox = provider.GetRequiredService<IInbox>();
            var processor = provider.GetRequiredService<IInboxProcessor>();
            var sagaStore = provider.GetRequiredService<ISagaStore>();

            const string correlationId = "order-sample-smoke-1";

            var metadata = InboxAcceptMetadata.Immediate with
            {
                Trace = new MessageTrace.Correlated(correlationId)
            };

            await inbox.AcceptAsync(InboxAcceptItem<AdvanceOrderSagaCommand>.From(new AdvanceOrderSagaCommand(Guid.NewGuid()), metadata)).ConfigureAwait(false);
            await inbox.AcceptAsync(InboxAcceptItem<AdvanceOrderSagaCommand>.From(new AdvanceOrderSagaCommand(Guid.NewGuid()), metadata)).ConfigureAwait(false);

            await processor.ProcessPendingAsync().ConfigureAwait(false);
            await processor.ProcessPendingAsync().ConfigureAwait(false);

            var instance = await sagaStore.LoadAsync<OrderSagaState>(
                new SagaCorrelation { CorrelationId = correlationId, SagaDefinitionId = OrderSagaContractName }).ConfigureAwait(false);

            Assert.NotNull(instance);
            instance.State.Step.Should().Be(2);
        }
    }
}
