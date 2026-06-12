using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Dispatch.InProcess;
using LiteBus.Messaging.Abstractions.DurableMessaging;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Dispatch.InProcess;
using LiteBus.Saga.Abstractions;
using LiteBus.Samples.V6;
using LiteBus.Samples.V6.Saga;
using LiteBus.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using IInboxProcessor = LiteBus.Inbox.Abstractions.IInboxProcessor;

namespace LiteBus.Composition.UnitTests;

/// <summary>
///     Smoke tests for the LiteBus v6 sample composition.
/// </summary>
public sealed class LiteBusV6CompositionSmokeTests : LiteBusTestBase
{
    /// <summary>
    ///     The saga contract name registered by the v6 sample.
    /// </summary>
    private const string OrderSagaContractName = "orders.saga.advance";

    /// <summary>
    ///     Verifies that the sample v6 composition registers inbox, outbox, dispatchers, saga services, and hosted processors.
    /// </summary>
    [Fact]
    public void AddLiteBusV6_ShouldRegisterCoreServicesAndHostedProcessors()
    {
        var services = new ServiceCollection();
        services.AddLiteBusV6(new ConfigurationBuilder().Build());

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IInbox>().Should().NotBeNull();
        provider.GetRequiredService<IOutbox>().Should().NotBeNull();
        provider.GetRequiredService<IInboxDispatcher>().Should().BeOfType<CommandInboxDispatcher>();
        provider.GetRequiredService<IOutboxDispatcher>().Should().BeOfType<EventOutboxDispatcher>();
        provider.GetRequiredService<ISagaStore>().Should().NotBeNull();
        provider.GetRequiredService<ISagaContext>().Should().NotBeNull();

        provider.GetServices<IHostedService>().Should().HaveCountGreaterThanOrEqualTo(2);
    }

    /// <summary>
    ///     Verifies that two correlated inbox commands advance saga state through the sample composition.
    /// </summary>
    [Fact]
    public async Task AddLiteBusV6_ShouldPersistSagaStateAcrossCorrelatedCommands()
    {
        var services = new ServiceCollection();
        services.AddLiteBusV6(new ConfigurationBuilder().Build());

        await using var provider = services.BuildServiceProvider();

        var inbox = provider.GetRequiredService<IInbox>();
        var processor = provider.GetRequiredService<IInboxProcessor>();
        var sagaStore = provider.GetRequiredService<ISagaStore>();

        const string correlationId = "order-sample-smoke-1";

        var metadata = InboxAcceptMetadata.Immediate with
        {
            Trace = new MessageTrace.Correlated(correlationId)
        };

        await inbox.AcceptAsync(InboxAcceptItem<AdvanceOrderSagaCommand>.From(new AdvanceOrderSagaCommand(Guid.NewGuid()), metadata));
        await inbox.AcceptAsync(InboxAcceptItem<AdvanceOrderSagaCommand>.From(new AdvanceOrderSagaCommand(Guid.NewGuid()), metadata));

        await processor.ProcessPendingAsync();
        await processor.ProcessPendingAsync();

        var instance = await sagaStore.LoadAsync<OrderSagaState>(
            new SagaCorrelation { CorrelationId = correlationId, SagaDefinitionId = OrderSagaContractName });

        Assert.NotNull(instance);
        instance.State.Step.Should().Be(2);
    }
}