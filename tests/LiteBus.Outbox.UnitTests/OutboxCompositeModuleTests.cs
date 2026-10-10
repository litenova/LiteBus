using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Processing;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.InMemory;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Outbox.UnitTests;

public sealed class OutboxCompositeModuleTests
{
    [Fact]
    public void ProcessorOptions_WhenSetBeforeDispatcher_ShouldPreserveApplicationPolicy()
    {
        var builder = new OutboxModuleBuilder();
        builder.UseProcessorOptions(new OutboxProcessorOptions
        {
            HookFailurePolicy = ProcessorHookFailurePolicy.DeadLetter
        });
        builder.RegisterDispatcher(new RecommendingDispatcherModule());

        builder.ProcessorOptions.HookFailurePolicy.Should().Be(ProcessorHookFailurePolicy.DeadLetter);
    }

    [Fact]
    public void ProcessorOptions_WhenSetAfterDispatcher_ShouldPreserveApplicationPolicy()
    {
        var builder = new OutboxModuleBuilder();
        builder.RegisterDispatcher(new RecommendingDispatcherModule());
        builder.UseProcessorOptions(new OutboxProcessorOptions
        {
            HookFailurePolicy = ProcessorHookFailurePolicy.DeadLetter
        });

        builder.ProcessorOptions.HookFailurePolicy.Should().Be(ProcessorHookFailurePolicy.DeadLetter);
    }

    [Fact]
    public void ProcessorOptions_WhenNotSet_ShouldUseDispatcherRecommendation()
    {
        var builder = new OutboxModuleBuilder();
        builder.RegisterDispatcher(new RecommendingDispatcherModule());

        builder.ProcessorOptions.HookFailurePolicy.Should()
            .Be(ProcessorHookFailurePolicy.CompleteDespiteHookFailure);
    }

    [Fact]
    public void InMemoryStorageModule_WhenRegisteredWithoutOutboxCore_ShouldThrow()
    {
        var act = () =>
        {
            new ServiceCollection()
                .AddLiteBus(registry => registry.Modules.Register(new InMemoryOutboxStorageModule(_ =>
                {
                })))
                .BuildServiceProvider();
        };

        act.Should()
            .Throw<LiteBusConfigurationException>()
            .WithMessage("*requires 'OutboxModule'*");
    }

    [Fact]
    public async Task AddOutboxModule_WithNestedStorageAndDispatcher_ShouldResolveOutboxServices()
    {
        var dispatcherHolder = new OutboxTestInfrastructure.RecordingOutboxDispatcherHolder();

        var services = new ServiceCollection()
            .AddSingleton(dispatcherHolder)
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ =>
                {
                });

                registry.AddOutbox(outbox =>
                {
                    outbox.Contracts.Register<OutboxTests.OrderSubmittedIntegrationEvent>(
                        "orders.events.submitted");

                    outbox.UseProcessorOptions(new OutboxProcessorOptions
                    {
                        BatchSize = 10,
                        LeaseOwner = "test-publisher",
                        Retry = new RetryOptions { UseJitter = false }
                    });

                    outbox.UseInMemoryStorage();
                    outbox.UseRecordingOutboxDispatcher(dispatcherHolder);
                });
            });

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOutbox>().Should().NotBeNull();
        provider.GetRequiredService<IOutboxStore>().Should().NotBeNull();
        provider.GetRequiredService<IOutboxLeaseStore>().Should().NotBeNull();
        provider.GetRequiredService<IOutboxStateWriter>().Should().NotBeNull();
        provider.GetRequiredService<IOutboxDeadLetterStore>().Should().NotBeNull();
        provider.GetRequiredService<IOutboxRetentionStore>().Should().NotBeNull();
        provider.GetRequiredService<IOutboxDiagnosticsStore>().Should().NotBeNull();
        provider.GetRequiredService<IOutboxDispatcher>().Should().BeSameAs(dispatcherHolder.Instance);

        var outbox = provider.GetRequiredService<IOutbox>();
        var orderId = Guid.NewGuid();

        await outbox.EnqueueAsync(OutboxWriterTestFactory.ItemWithId(
            new OutboxTests.OrderSubmittedIntegrationEvent { OrderId = orderId },
            Guid.NewGuid())).ConfigureAwait(false);

        var result = await provider.GetRequiredService<IOutboxProcessor>().ProcessPendingAsync().ConfigureAwait(false);

        result.SucceededCount.Should().Be(1);
        dispatcherHolder.Instance!.DispatchedMessages
            .OfType<OutboxTests.OrderSubmittedIntegrationEvent>()
            .Should()
            .ContainSingle(submitted => submitted.OrderId == orderId);
    }

    private sealed class RecommendingDispatcherModule : IOutboxDispatcherModule
    {
        public ProcessorHookFailurePolicy DefaultHookFailurePolicy =>
            ProcessorHookFailurePolicy.CompleteDespiteHookFailure;

        public void Build(IModuleConfiguration configuration)
        {
        }
    }
}
