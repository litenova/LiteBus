using LiteBus.Events;
using LiteBus.Events.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Dispatch.InProcess;
using LiteBus.Outbox.Storage.InMemory;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Outbox.UnitTests.ImmediateDispatch;

/// <summary>
///     Verifies composition validation for immediate dispatch requirements and per-handler subscriber names.
/// </summary>
public sealed class ImmediateDispatchCompositionTests
{
    /// <summary>
    ///     A subscriber name of 250 characters, exceeding the 200 character limit.
    /// </summary>
    private const string OverlongSubscriberName =
        "aaaaaaaaaa" + "aaaaaaaaaa" + "aaaaaaaaaa" + "aaaaaaaaaa" + "aaaaaaaaaa" +
        "aaaaaaaaaa" + "aaaaaaaaaa" + "aaaaaaaaaa" + "aaaaaaaaaa" + "aaaaaaaaaa" +
        "aaaaaaaaaa" + "aaaaaaaaaa" + "aaaaaaaaaa" + "aaaaaaaaaa" + "aaaaaaaaaa" +
        "aaaaaaaaaa" + "aaaaaaaaaa" + "aaaaaaaaaa" + "aaaaaaaaaa" + "aaaaaaaaaa" +
        "aaaaaaaaaa" + "aaaaaaaaaa" + "aaaaaaaaaa" + "aaaaaaaaaa" + "aaaaaaaaaa";

    /// <summary>
    ///     Verifies that enabling immediate dispatch without a unit of work fails composition.
    /// </summary>
    [Fact]
    public void EnableImmediateDispatch_WithoutUnitOfWork_ShouldThrowNamingUnitOfWork()
    {
        var act = () =>
        {
            _ = new ServiceCollection().AddLiteBus(registry =>
            {
                registry.AddMessaging(_ =>
                {
                });

                registry.AddEvents(_ => { });
                registry.AddOutbox(builder =>
                {
                    builder.UseInMemoryStorage();
                    builder.UseInProcessDispatch();
                    builder.EnableImmediateDispatch();
                });
            });
        };

        act.Should()
            .Throw<LiteBusConfigurationException>()
            .WithMessage("*unit of work*");
    }

    /// <summary>
    ///     Verifies that enabling immediate dispatch without a dispatcher fails composition.
    /// </summary>
    [Fact]
    public void EnableImmediateDispatch_WithoutDispatcher_ShouldThrowNamingDispatcher()
    {
        var act = () =>
        {
            _ = new ServiceCollection().AddLiteBus(registry =>
            {
                registry.Modules.Register(new UnitOfWorkStubModule());

                registry.AddMessaging(_ =>
                {
                });

                registry.AddEvents(_ => { });
                registry.AddOutbox(builder =>
                {
                    builder.UseInMemoryStorage();
                    builder.EnableImmediateDispatch();
                });
            });
        };

        act.Should()
            .Throw<LiteBusConfigurationException>()
            .WithMessage("*outbox dispatcher*");
    }

    /// <summary>
    ///     Verifies that enabling immediate dispatch with a store lacking targeted leases fails composition.
    /// </summary>
    [Fact]
    public void EnableImmediateDispatch_WithoutTargetedLeaseStore_ShouldThrowNamingTargetedLeaseStore()
    {
        var act = () =>
        {
            _ = new ServiceCollection().AddLiteBus(registry =>
            {
                registry.Modules.Register(new UnitOfWorkStubModule());

                registry.AddMessaging(_ =>
                {
                });

                registry.AddEvents(_ => { });
                registry.AddOutbox(builder =>
                {
                    builder.RegisterStorage(new NonTargetedOutboxStorageModule());
                    builder.UseInProcessDispatch();
                    builder.EnableImmediateDispatch();
                });
            });
        };

        act.Should()
            .Throw<LiteBusConfigurationException>()
            .WithMessage("*IOutboxTargetedLeaseStore*");
    }

    /// <summary>
    ///     Verifies that a fallback delay shorter than the timeout fails composition.
    /// </summary>
    [Fact]
    public void EnableImmediateDispatch_FallbackDelayShorterThanTimeout_ShouldThrow()
    {
        var act = () =>
        {
            _ = new ServiceCollection().AddLiteBus(registry =>
            {
                registry.AddMessaging(_ =>
                {
                });

                registry.AddEvents(_ => { });
                registry.AddOutbox(builder =>
                {
                    builder.UseInMemoryStorage();
                    builder.UseInProcessDispatch();
                    builder.EnableImmediateDispatch(options =>
                    {
                        options.Timeout = TimeSpan.FromSeconds(30);
                        options.FallbackDelay = TimeSpan.FromSeconds(10);
                    });
                });
            });
        };

        act.Should()
            .Throw<LiteBusConfigurationException>()
            .WithMessage("*FallbackDelay*must be greater than Timeout*");
    }

    /// <summary>
    ///     Verifies that a fallback delay equal to the timeout fails composition.
    /// </summary>
    [Fact]
    public void EnableImmediateDispatch_FallbackDelayEqualToTimeout_ShouldThrow()
    {
        var act = () =>
        {
            _ = new ServiceCollection().AddLiteBus(registry =>
            {
                registry.AddMessaging(_ =>
                {
                });

                registry.AddEvents(_ => { });
                registry.AddOutbox(builder =>
                {
                    builder.UseInMemoryStorage();
                    builder.UseInProcessDispatch();
                    builder.EnableImmediateDispatch(options =>
                    {
                        options.Timeout = TimeSpan.FromSeconds(10);
                        options.FallbackDelay = TimeSpan.FromSeconds(10);
                    });
                });
            });
        };

        act.Should()
            .Throw<LiteBusConfigurationException>()
            .WithMessage("*FallbackDelay*must be greater than Timeout*");
    }

    /// <summary>
    ///     Verifies that enabling immediate dispatch with a unit of work, a dispatcher, and a targeted lease store succeeds.
    /// </summary>
    [Fact]
    public void EnableImmediateDispatch_WithAllRequirements_ShouldSucceed()
    {
        var act = () =>
        {
            _ = new ServiceCollection().AddLiteBus(registry =>
            {
                registry.Modules.Register(new UnitOfWorkStubModule());

                registry.AddMessaging(_ =>
                {
                });

                registry.AddEvents(_ => { });
                registry.AddOutbox(builder =>
                {
                    builder.UseInMemoryStorage();
                    builder.UseInProcessDispatch();
                    builder.EnableImmediateDispatch();
                });
            });
        };

        act.Should().NotThrow();
    }

    /// <summary>
    ///     Verifies that a subscriber name with characters outside the permitted set fails composition.
    /// </summary>
    [Fact]
    public void EnablePerHandlerDelivery_InvalidSubscriberName_ShouldThrowNamingHandler()
    {
        var act = () =>
        {
            _ = new ServiceCollection().AddLiteBus(registry =>
            {
                registry.AddMessaging(_ =>
                {
                });

                registry.AddEvents(builder =>
                {
                    builder.Register<InvalidlyNamedHandler>();
                });

                registry.AddOutbox(builder =>
                {
                    builder.UseInMemoryStorage();
                    builder.EnablePerHandlerDelivery();
                });
            });
        };

        act.Should()
            .Throw<LiteBusConfigurationException>()
            .WithMessage("*declares an invalid subscriber name 'bad name!'*");
    }

    /// <summary>
    ///     Verifies that an empty subscriber name fails composition.
    /// </summary>
    [Fact]
    public void EnablePerHandlerDelivery_EmptySubscriberName_ShouldThrow()
    {
        var act = () =>
        {
            _ = new ServiceCollection().AddLiteBus(registry =>
            {
                registry.AddMessaging(_ =>
                {
                });

                registry.AddEvents(builder =>
                {
                    builder.Register<EmptyNamedHandler>();
                });

                registry.AddOutbox(builder =>
                {
                    builder.UseInMemoryStorage();
                    builder.EnablePerHandlerDelivery();
                });
            });
        };

        act.Should()
            .Throw<LiteBusConfigurationException>()
            .WithMessage("*declares an invalid subscriber name*non-empty*");
    }

    /// <summary>
    ///     Verifies that a subscriber name longer than 200 characters fails composition.
    /// </summary>
    [Fact]
    public void EnablePerHandlerDelivery_OverlongSubscriberName_ShouldThrow()
    {
        var act = () =>
        {
            _ = new ServiceCollection().AddLiteBus(registry =>
            {
                registry.AddMessaging(_ =>
                {
                });

                registry.AddEvents(builder =>
                {
                    builder.Register<OverlongNamedHandler>();
                });

                registry.AddOutbox(builder =>
                {
                    builder.UseInMemoryStorage();
                    builder.EnablePerHandlerDelivery();
                });
            });
        };

        act.Should()
            .Throw<LiteBusConfigurationException>()
            .WithMessage("*declares an invalid subscriber name*at most 200 characters*");
    }

    /// <summary>
    ///     Verifies that two handlers of one event sharing a subscriber name fail composition naming both handlers.
    /// </summary>
    [Fact]
    public void EnablePerHandlerDelivery_DuplicateSubscriberName_ShouldThrowNamingBothHandlers()
    {
        var act = () =>
        {
            _ = new ServiceCollection().AddLiteBus(registry =>
            {
                registry.AddMessaging(_ =>
                {
                });

                registry.AddEvents(builder =>
                {
                    builder.Register<FirstBillingHandler>();
                    builder.Register<SecondBillingHandler>();
                });

                registry.AddOutbox(builder =>
                {
                    builder.UseInMemoryStorage();
                    builder.EnablePerHandlerDelivery();
                });
            });
        };

        act.Should()
            .Throw<LiteBusConfigurationException>()
            .WithMessage("*FirstBillingHandler*SecondBillingHandler*");
    }

    /// <summary>
    ///     Verifies that a subscriber name equal to another handler's full type name fails composition.
    /// </summary>
    [Fact]
    public void EnablePerHandlerDelivery_SubscriberNameEqualToHandlerTypeName_ShouldThrow()
    {
        var act = () =>
        {
            _ = new ServiceCollection().AddLiteBus(registry =>
            {
                registry.AddMessaging(_ =>
                {
                });

                registry.AddEvents(builder =>
                {
                    builder.Register<ShadowedHandler>();
                    builder.Register<ShadowingHandler>();
                });

                registry.AddOutbox(builder =>
                {
                    builder.UseInMemoryStorage();
                    builder.EnablePerHandlerDelivery();
                });
            });
        };

        act.Should()
            .Throw<LiteBusConfigurationException>()
            .WithMessage("*equals the full type name*");
    }

    /// <summary>
    ///     Verifies that distinct valid subscriber names succeed composition.
    /// </summary>
    [Fact]
    public void EnablePerHandlerDelivery_DistinctSubscriberNames_ShouldSucceed()
    {
        var act = () =>
        {
            _ = new ServiceCollection().AddLiteBus(registry =>
            {
                registry.AddMessaging(_ =>
                {
                });

                registry.AddEvents(builder =>
                {
                    builder.Register<FirstBillingHandler>();
                    builder.Register<ShippingHandler>();
                    builder.Register<ShadowedHandler>();
                });

                registry.AddOutbox(builder =>
                {
                    builder.UseInMemoryStorage();
                    builder.EnablePerHandlerDelivery();
                });
            });
        };

        act.Should().NotThrow();
    }

    /// <summary>
    ///     Registers a unit of work so immediate dispatch compositions declare one without the command module.
    /// </summary>
    private sealed class UnitOfWorkStubModule : IModule
    {
        /// <inheritdoc />
        public void Build(IModuleConfiguration configuration)
        {
            configuration.DependencyRegistry.Register(new DependencyDescriptor(
                typeof(IUnitOfWork),
                typeof(StubUnitOfWork),
                InstanceLifetime.Scoped));
        }
    }

    /// <summary>
    ///     A unit of work that commits without touching storage.
    /// </summary>
    private sealed class StubUnitOfWork : IUnitOfWork
    {
        /// <inheritdoc />
        public Task CommitAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>
    ///     A storage module that registers no store, standing in for a store without targeted leases.
    /// </summary>
    private sealed class NonTargetedOutboxStorageModule : IOutboxStorageModule
    {
        /// <inheritdoc />
        /// <remarks>
        ///     The build registers nothing on purpose: satisfying the storage requirement while leaving
        ///     <c>IOutboxTargetedLeaseStore</c> unregistered is what the missing store composition test needs.
        /// </remarks>
        public void Build(IModuleConfiguration configuration)
        {
            // Registers nothing: the missing targeted lease store is the case under test.
        }
    }

    /// <summary>
    ///     An event observed by the subscriber name validation handlers.
    /// </summary>
    private sealed record SubscriberProbeEvent;

    /// <summary>
    ///     A handler whose subscriber name uses characters outside the permitted set.
    /// </summary>
    [SubscriberName("bad name!")]
    private sealed class InvalidlyNamedHandler : IEventHandler<SubscriberProbeEvent>
    {
        /// <inheritdoc />
        public Task HandleAsync(SubscriberProbeEvent message, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>
    ///     A handler whose subscriber name is empty.
    /// </summary>
    [SubscriberName("")]
    private sealed class EmptyNamedHandler : IEventHandler<SubscriberProbeEvent>
    {
        /// <inheritdoc />
        public Task HandleAsync(SubscriberProbeEvent message, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>
    ///     A handler whose subscriber name exceeds the 200 character limit.
    /// </summary>
    [SubscriberName(OverlongSubscriberName)]
    private sealed class OverlongNamedHandler : IEventHandler<SubscriberProbeEvent>
    {
        /// <inheritdoc />
        public Task HandleAsync(SubscriberProbeEvent message, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>
    ///     The first handler sharing the billing subscriber name.
    /// </summary>
    [SubscriberName("billing")]
    private sealed class FirstBillingHandler : IEventHandler<SubscriberProbeEvent>
    {
        /// <inheritdoc />
        public Task HandleAsync(SubscriberProbeEvent message, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>
    ///     The second handler sharing the billing subscriber name.
    /// </summary>
    [SubscriberName("billing")]
    private sealed class SecondBillingHandler : IEventHandler<SubscriberProbeEvent>
    {
        /// <inheritdoc />
        public Task HandleAsync(SubscriberProbeEvent message, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>
    ///     A handler with a subscriber name distinct from the billing handlers.
    /// </summary>
    [SubscriberName("shipping")]
    private sealed class ShippingHandler : IEventHandler<SubscriberProbeEvent>
    {
        /// <inheritdoc />
        public Task HandleAsync(SubscriberProbeEvent message, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>
    ///     A handler recorded under its full type name.
    /// </summary>
    private sealed class ShadowedHandler : IEventHandler<SubscriberProbeEvent>
    {
        /// <inheritdoc />
        public Task HandleAsync(SubscriberProbeEvent message, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>
    ///     A handler whose subscriber name equals the full type name of <see cref="ShadowedHandler" />.
    /// </summary>
    [SubscriberName("LiteBus.Outbox.UnitTests.ImmediateDispatch.ImmediateDispatchCompositionTests+ShadowedHandler")]
    private sealed class ShadowingHandler : IEventHandler<SubscriberProbeEvent>
    {
        /// <inheritdoc />
        public Task HandleAsync(SubscriberProbeEvent message, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
