using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions.Processing;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.InMemory;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Outbox.UnitTests.ReleaseFixes;

/// <summary>
///     Verifies per-handler delivery plus a transport dispatcher fails composition unless explicitly acknowledged.
/// </summary>
public sealed class R3F14PerHandlerTransportGuardTests
{
    /// <summary>
    ///     Verifies the combination throws at build time.
    /// </summary>
    [Fact]
    public void AddOutbox_with_per_handler_and_transport_dispatch_should_throw()
    {
        var act = () =>
        {
            new ServiceCollection()
                .AddLiteBus(registry =>
                {
                    registry.AddMessaging(_ =>
                    {
                    });

                    registry.AddOutbox(builder =>
                    {
                        builder.UseInMemoryStorage();
                        builder.RegisterDispatcher(new TransportLikeDispatcherModule());
                        builder.EnablePerHandlerDelivery();
                    });
                })
                .BuildServiceProvider();
        };

        act.Should().Throw<DurableStorageConfigurationException>()
            .WithMessage("*AllowPerHandlerDeliveryWithTransportDispatch*");
    }

    /// <summary>
    ///     Verifies the explicit acknowledgement lets the combination compose.
    /// </summary>
    [Fact]
    public void AddOutbox_with_acknowledged_duplication_should_compose()
    {
        using var provider = new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ =>
                {
                });

                registry.AddOutbox(builder =>
                {
                    builder.UseInMemoryStorage();
                    builder.RegisterDispatcher(new TransportLikeDispatcherModule());
                    builder.EnablePerHandlerDelivery();
                    builder.AllowPerHandlerDeliveryWithTransportDispatch();
                });
            })
            .BuildServiceProvider();

        provider.GetRequiredService<IOutbox>().Should().NotBeNull();
    }

    /// <summary>
    ///     Verifies per-handler delivery with in-process dispatch still composes without acknowledgement.
    /// </summary>
    [Fact]
    public void AddOutbox_with_per_handler_and_in_process_dispatch_should_compose()
    {
        using var provider = new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ =>
                {
                });

                registry.AddOutbox(builder =>
                {
                    builder.UseInMemoryStorage();
                    builder.RegisterDispatcher(new InProcessLikeDispatcherModule());
                    builder.EnablePerHandlerDelivery();
                });
            })
            .BuildServiceProvider();

        provider.GetRequiredService<IOutbox>().Should().NotBeNull();
    }

    private sealed class TransportLikeDispatcherModule : IOutboxDispatcherModule
    {
        public ProcessorHookFailurePolicy DefaultHookFailurePolicy =>
            ProcessorHookFailurePolicy.CompleteDespiteHookFailure;

        public bool PublishesFullEventPerEnvelope => true;

        public void Build(IModuleConfiguration configuration)
        {
        }
    }

    private sealed class InProcessLikeDispatcherModule : IOutboxDispatcherModule
    {
        public void Build(IModuleConfiguration configuration)
        {
        }
    }
}
