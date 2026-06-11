using LiteBus.Events;
using LiteBus.Events.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.DurableMessaging;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.InMemory;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Outbox.Dispatch.InProcess.UnitTests;

[Collection("Sequential")]
public sealed class InProcessOutboxDispatcherTests : LiteBusTestBase
{
    [Fact]
    public async Task ProcessPendingAsync_ShouldPublishThroughInProcessOutboxDispatcherAndMarkPublished()
    {
        var recorder = new EventRecorder();

        var serviceProvider = new ServiceCollection()
            .AddSingleton(recorder)
            .AddLiteBus(registry =>
            {
                registry.AddMessageModule(_ =>
                {
                });

                registry.AddEventModule(builder =>
                {
                    builder.Register<OrderSubmittedEventHandler>();
                });

                registry.AddOutboxModule(builder =>
                {
                    builder.Contracts.Register<OrderSubmittedIntegrationEvent>("orders.events.submitted");

                    builder.UseProcessorOptions(new OutboxProcessorOptions
                    {
                        BatchSize = 10,
                        LeaseOwner = "test-publisher",
                        Retry = new RetryOptions { UseJitter = false }
                    });

                    builder.UseInMemoryStorage();
                    builder.UseEventOutboxDispatcher();
                });
            })
            .BuildServiceProvider();

        var outbox = serviceProvider.GetRequiredService<IOutbox>();
        var processor = serviceProvider.GetRequiredService<IOutboxProcessor>();
        var eventId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        await outbox.EnqueueAsync(
            OutboxEnqueueItems.WithIdentity(new OrderSubmittedIntegrationEvent { OrderId = orderId }, eventId));

        await processor.ProcessPendingAsync();

        recorder.Events.Should().ContainSingle(@event => @event.OrderId == orderId);

        var store = serviceProvider.GetRequiredService<InMemoryOutboxStore>();
        var envelope = store.Get(eventId);
        envelope.Status.Should().Be(OutboxStatus.Published);
        envelope.AttemptCount.Should().Be(1);
        envelope.LeaseOwner.Should().BeNull();
    }

    [Fact]
    public async Task InProcessOutboxDispatcher_ShouldPublishPocoEvent()
    {
        var recorder = new PocoEventRecorder();

        var serviceProvider = new ServiceCollection()
            .AddSingleton(recorder)
            .AddLiteBus(registry =>
            {
                registry.AddMessageModule(_ =>
                {
                });

                registry.AddEventModule(builder => builder.Register<PocoEventHandler>());

                registry.AddOutboxModule(builder =>
                {
                    builder.Contracts.Register<PocoIntegrationEvent>("poco.events.sample");

                    builder.UseProcessorOptions(new OutboxProcessorOptions
                    {
                        BatchSize = 10,
                        LeaseOwner = "poco-publisher",
                        Retry = new RetryOptions { UseJitter = false }
                    });

                    builder.UseInMemoryStorage();
                    builder.UseEventOutboxDispatcher();
                });
            })
            .BuildServiceProvider();

        var writer = serviceProvider.GetRequiredService<IOutbox>();
        var processor = serviceProvider.GetRequiredService<IOutboxProcessor>();
        var messageId = Guid.NewGuid();

        await writer.EnqueueAsync(
            OutboxEnqueueItems.WithIdentity(new PocoIntegrationEvent { Value = "poco-test" }, messageId));

        await processor.ProcessPendingAsync();

        recorder.Values.Should().ContainSingle("poco-test");
    }

    [Fact]
    public async Task InProcessOutboxDispatcher_ShouldCopyTraceMetadataIntoMediationSettings()
    {
        var capture = new TraceMetadataCapture();

        var serviceProvider = new ServiceCollection()
            .AddSingleton(capture)
            .AddLiteBus(registry =>
            {
                registry.AddMessageModule(_ =>
                {
                });

                registry.AddEventModule(builder => builder.Register<TraceMetadataEventHandler>());

                registry.AddOutboxModule(builder =>
                {
                    builder.Contracts.Register<OrderSubmittedIntegrationEvent>("orders.events.submitted");

                    builder.UseProcessorOptions(new OutboxProcessorOptions
                    {
                        BatchSize = 10,
                        LeaseOwner = "trace-publisher",
                        Retry = new RetryOptions { UseJitter = false }
                    });

                    builder.UseInMemoryStorage();
                    builder.UseEventOutboxDispatcher();
                });
            })
            .BuildServiceProvider();

        var writer = serviceProvider.GetRequiredService<IOutbox>();
        var processor = serviceProvider.GetRequiredService<IOutboxProcessor>();

        await writer.EnqueueAsync(
            OutboxEnqueueItems.WithMetadata(
                new OrderSubmittedIntegrationEvent { OrderId = Guid.NewGuid() },
                new OutboxEnqueueMetadata
                {
                    Identity = new MessageIdentity.Supplied(Guid.NewGuid()),
                    Idempotency = Idempotency.None.Instance,
                    Visibility = MessageVisibility.Immediate.Instance,
                    Trace = new MessageTrace.Workflow("correlation-42", "causation-7"),
                    Tenant = new TenantScope.Isolated("tenant-west"),
                    Target = PublicationTarget.ContractDefault.Instance
                }));

        await processor.ProcessPendingAsync();

        capture.CorrelationId.Should().Be("correlation-42");
        capture.CausationId.Should().Be("causation-7");
        capture.TenantId.Should().Be("tenant-west");
    }

    [Fact]
    public void AddOutboxInProcessDispatcher_ShouldRegisterInProcessOutboxDispatcher()
    {
        var serviceProvider = new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessageModule(_ =>
                {
                });

                registry.AddEventModule();

                registry.AddOutboxModule(outbox =>
                {
                    outbox.UseInMemoryStorage();
                    outbox.UseEventOutboxDispatcher();
                });
            })
            .BuildServiceProvider();

        serviceProvider.GetRequiredService<IOutboxDispatcher>().Should().BeOfType<EventOutboxDispatcher>();
    }

    [Fact]
    public void AddOutboxInProcessDispatcher_WhenAnotherDispatcherRegistered_ShouldThrow()
    {
        var act = () => new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessageModule(_ =>
                {
                });

                registry.AddEventModule();
                registry.AddOutboxModule(outbox => outbox.UseInMemoryStorage());
                registry.Register(new PreRegisteredOutboxDispatcherModule());
                registry.Register(new EventOutboxDispatchModule());
            })
            .BuildServiceProvider();

        act.Should().Throw<LiteBusConfigurationException>()
            .WithMessage("*IOutboxDispatcher*");
    }

    [Fact]
    public void AddOutboxInProcessDispatcher_WhenCalledTwice_ShouldThrow()
    {
        var act = () =>
        {
            new ServiceCollection()
                .AddLiteBus(registry =>
                {
                    registry.AddMessageModule(_ =>
                    {
                    });

                    registry.AddEventModule();

                    registry.AddOutboxModule(outbox =>
                    {
                        outbox.UseInMemoryStorage();
                        outbox.UseEventOutboxDispatcher();
                        outbox.UseEventOutboxDispatcher();
                    });
                });
        };

        act.Should().Throw<LiteBusConfigurationException>()
            .WithMessage("*Outbox dispatcher is already configured*");
    }

    private sealed class PreRegisteredOutboxDispatcherModule : IModule
    {
        public void Build(IModuleConfiguration configuration)
        {
            configuration.DependencyRegistry.Register(new DependencyDescriptor(
                typeof(IOutboxDispatcher),
                typeof(StubOutboxDispatcher)));
        }
    }

    private sealed class StubOutboxDispatcher : IOutboxDispatcher
    {
        public Task DispatchAsync(OutboxEnvelope envelope, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    public sealed record OrderSubmittedIntegrationEvent
    {
        public Guid OrderId { get; init; }
    }

    public sealed class OrderSubmittedEventHandler : IEventHandler<OrderSubmittedIntegrationEvent>
    {
        private readonly EventRecorder _recorder;

        public OrderSubmittedEventHandler(EventRecorder recorder)
        {
            _recorder = recorder;
        }

        public Task HandleAsync(OrderSubmittedIntegrationEvent message, CancellationToken cancellationToken = default)
        {
            _recorder.Record(message);
            return Task.CompletedTask;
        }
    }

    public sealed class EventRecorder
    {
        private readonly List<OrderSubmittedIntegrationEvent> _events = [];

        public IReadOnlyList<OrderSubmittedIntegrationEvent> Events => _events;

        public void Record(OrderSubmittedIntegrationEvent @event)
        {
            _events.Add(@event);
        }
    }

    public sealed record PocoIntegrationEvent
    {
        public required string Value { get; init; }
    }

    public sealed class PocoEventHandler : IEventHandler<PocoIntegrationEvent>
    {
        private readonly PocoEventRecorder _recorder;

        public PocoEventHandler(PocoEventRecorder recorder)
        {
            _recorder = recorder;
        }

        public Task HandleAsync(PocoIntegrationEvent message, CancellationToken cancellationToken = default)
        {
            _recorder.Record(message.Value);
            return Task.CompletedTask;
        }
    }

    public sealed class PocoEventRecorder
    {
        private readonly List<string> _values = [];

        public IReadOnlyList<string> Values => _values;

        public void Record(string value)
        {
            _values.Add(value);
        }
    }

    public sealed class TraceMetadataCapture
    {
        public string? CorrelationId { get; set; }

        public string? CausationId { get; set; }

        public string? TenantId { get; set; }
    }

    public sealed class TraceMetadataEventHandler : IEventHandler<OrderSubmittedIntegrationEvent>
    {
        private readonly TraceMetadataCapture _capture;

        public TraceMetadataEventHandler(TraceMetadataCapture capture)
        {
            _capture = capture;
        }

        public Task HandleAsync(OrderSubmittedIntegrationEvent message, CancellationToken cancellationToken = default)
        {
            var items = AmbientExecutionContext.Current.Items;

            _capture.CorrelationId = items.TryGetValue(MessageTraceContextKeys.CorrelationId, out var correlation)
                ? correlation as string
                : null;

            _capture.CausationId = items.TryGetValue(MessageTraceContextKeys.CausationId, out var causation)
                ? causation as string
                : null;

            _capture.TenantId = items.TryGetValue(MessageTraceContextKeys.TenantId, out var tenant)
                ? tenant as string
                : null;

            return Task.CompletedTask;
        }
    }
}