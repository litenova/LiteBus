using LiteBus.Events;
using LiteBus.Events.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox.Dispatch.InProcess;
using LiteBus.Outbox.Storage.InMemory;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Outbox.UnitTests.Version8;

/// <summary>
///     Verifies composition fails when immediate dispatch is enabled without per-handler delivery while a handler carries the immediate dispatch attribute.
/// </summary>
public sealed class ImmediateDispatchHandlerAttributeCompositionTests
{
    /// <summary>
    ///     Verifies that enabling immediate dispatch without per-handler delivery fails composition naming the attributed handler.
    /// </summary>
    [Fact]
    public void EnableImmediateDispatch_WithoutPerHandlerDeliveryAndAttributedHandler_ShouldThrowNamingHandler()
    {
        var act = () =>
        {
            _ = new ServiceCollection().AddLiteBus(registry =>
            {
                registry.Modules.Register(new UnitOfWorkStubModule());

                registry.AddMessaging(_ =>
                {
                });

                registry.AddEvents(builder =>
                {
                    builder.Register<ImmediateProjection>();
                    builder.Register<DeferredProjection>();
                });

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
            .WithMessage("*ImmediateProjection*EnablePerHandlerDelivery()*");
    }

    /// <summary>
    ///     Verifies that two attributed handlers are both named in the composition failure.
    /// </summary>
    [Fact]
    public void EnableImmediateDispatch_WithoutPerHandlerDeliveryAndTwoAttributedHandlers_ShouldThrowNamingBoth()
    {
        var act = () =>
        {
            _ = new ServiceCollection().AddLiteBus(registry =>
            {
                registry.Modules.Register(new UnitOfWorkStubModule());

                registry.AddMessaging(_ =>
                {
                });

                registry.AddEvents(builder =>
                {
                    builder.Register<ImmediateProjection>();
                    builder.Register<ImmediateLedger>();
                });

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
            .Which.Message.Should().Contain("ImmediateProjection").And.Contain("ImmediateLedger");
    }

    /// <summary>
    ///     Verifies that per-handler delivery lets an attributed handler compose with immediate dispatch.
    /// </summary>
    [Fact]
    public void EnableImmediateDispatch_WithPerHandlerDeliveryAndAttributedHandler_ShouldSucceed()
    {
        var act = () =>
        {
            _ = new ServiceCollection().AddLiteBus(registry =>
            {
                registry.Modules.Register(new UnitOfWorkStubModule());

                registry.AddMessaging(_ =>
                {
                });

                registry.AddEvents(builder =>
                {
                    builder.Register<ImmediateProjection>();
                });

                registry.AddOutbox(builder =>
                {
                    builder.UseInMemoryStorage();
                    builder.UseInProcessDispatch();
                    builder.EnablePerHandlerDelivery();
                    builder.EnableImmediateDispatch();
                });
            });
        };

        act.Should().NotThrow();
    }

    /// <summary>
    ///     Verifies that an attributed event type composes with immediate dispatch without per-handler delivery.
    /// </summary>
    [Fact]
    public void EnableImmediateDispatch_WithoutPerHandlerDeliveryAndAttributedEvent_ShouldSucceed()
    {
        var act = () =>
        {
            _ = new ServiceCollection().AddLiteBus(registry =>
            {
                registry.Modules.Register(new UnitOfWorkStubModule());

                registry.AddMessaging(_ =>
                {
                });

                registry.AddEvents(builder =>
                {
                    builder.Register<ImmediateEventReaction>();
                });

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
    ///     Verifies that an attributed handler composes without immediate dispatch.
    /// </summary>
    [Fact]
    public void AttributedHandler_WithoutImmediateDispatch_ShouldSucceed()
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
                    builder.Register<ImmediateProjection>();
                });

                registry.AddOutbox(builder =>
                {
                    builder.UseInMemoryStorage();
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
    ///     An event observed by the projection handlers.
    /// </summary>
    private sealed record ProjectionEvent;

    /// <summary>
    ///     A handler marked for immediate dispatch.
    /// </summary>
    [DispatchImmediately]
    private sealed class ImmediateProjection : IEventHandler<ProjectionEvent>
    {
        /// <inheritdoc />
        public Task HandleAsync(ProjectionEvent message, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>
    ///     A second handler marked for immediate dispatch.
    /// </summary>
    [DispatchImmediately]
    private sealed class ImmediateLedger : IEventHandler<ProjectionEvent>
    {
        /// <inheritdoc />
        public Task HandleAsync(ProjectionEvent message, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>
    ///     A handler without the immediate dispatch attribute.
    /// </summary>
    private sealed class DeferredProjection : IEventHandler<ProjectionEvent>
    {
        /// <inheritdoc />
        public Task HandleAsync(ProjectionEvent message, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>
    ///     An event type marked for immediate dispatch.
    /// </summary>
    [DispatchImmediately]
    private sealed record ImmediateEvent;

    /// <summary>
    ///     A handler observing the attributed event type.
    /// </summary>
    private sealed class ImmediateEventReaction : IEventHandler<ImmediateEvent>
    {
        /// <inheritdoc />
        public Task HandleAsync(ImmediateEvent message, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
