using LiteBus.Events;
using LiteBus.Events.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.InMemory;
using LiteBus.Outbox.Dispatch.InProcess;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Outbox.Dispatch.InProcess.UnitTests;

[Collection("Sequential")]
public sealed class InProcessOutboxDispatcherTests : LiteBusTestBase
{
    [Fact]
    public async Task ProcessPendingAsync_ShouldPublishThroughInProcessOutboxDispatcherAndMarkPublished()
    {
        var store = new InMemoryOutboxStore();
        var recorder = new EventRecorder();

        var serviceProvider = new ServiceCollection()
            .AddOutboxStoreRoles(store)
            .AddSingleton(recorder)
            .AddLiteBus(modules =>
            {
                modules.AddEventModule(builder =>
                {
                    builder.Register<OrderSubmittedEventHandler>();
                });

                modules.AddOutboxModule(builder =>
                {
                    builder.Contracts.Register<OrderSubmittedIntegrationEvent>("orders.events.submitted", 1);
                    builder.UseProcessorOptions(new OutboxProcessorOptions
                    {
                        BatchSize = 10,
                        LeaseOwner = "test-publisher",
                        Retry = new RetryOptions { UseJitter = false }
                    });
                    builder.UseInProcessDispatcher();
                });
            })
            .BuildServiceProvider();

        var outbox = serviceProvider.GetRequiredService<IOutbox>();
        var processor = serviceProvider.GetRequiredService<IOutboxProcessor>();
        var eventId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        await outbox.EnqueueAsync(new OrderSubmittedIntegrationEvent { OrderId = orderId }, new OutboxOptions { Id = eventId });
        await processor.ProcessPendingAsync();

        recorder.Events.Should().ContainSingle(@event => @event.OrderId == orderId);

        var envelope = store.Get(eventId);
        envelope.Status.Should().Be(OutboxStatus.Published);
        envelope.AttemptCount.Should().Be(1);
        envelope.LeaseOwner.Should().BeNull();
    }

    [Fact]
    public async Task InProcessOutboxDispatcher_ShouldPublishPocoEvent()
    {
        var store = new InMemoryOutboxStore();
        var recorder = new PocoEventRecorder();

        var serviceProvider = new ServiceCollection()
            .AddOutboxStoreRoles(store)
            .AddSingleton(recorder)
            .AddLiteBus(modules =>
            {
                modules.AddEventModule(builder => builder.Register<PocoEventHandler>());
                modules.AddOutboxModule(builder =>
                {
                    builder.Contracts.Register<PocoIntegrationEvent>("poco.events.sample", 1);
                    builder.UseProcessorOptions(new OutboxProcessorOptions
                    {
                        BatchSize = 10,
                        LeaseOwner = "poco-publisher",
                        Retry = new RetryOptions { UseJitter = false }
                    });
                    builder.UseInProcessDispatcher();
                });
            })
            .BuildServiceProvider();

        var writer = serviceProvider.GetRequiredService<IOutbox>();
        var processor = serviceProvider.GetRequiredService<IOutboxProcessor>();
        var messageId = Guid.NewGuid();

        await writer.EnqueueAsync(new PocoIntegrationEvent { Value = "poco-test" }, new OutboxOptions { Id = messageId });
        await processor.ProcessPendingAsync();

        recorder.Values.Should().ContainSingle("poco-test");
    }

    [Fact]
    public async Task InProcessOutboxDispatcher_ShouldCopyTraceMetadataIntoMediationSettings()
    {
        var store = new InMemoryOutboxStore();
        var capture = new TraceMetadataCapture();

        var serviceProvider = new ServiceCollection()
            .AddOutboxStoreRoles(store)
            .AddSingleton(capture)
            .AddLiteBus(modules =>
            {
                modules.AddEventModule(builder => builder.Register<TraceMetadataEventHandler>());
                modules.AddOutboxModule(builder =>
                {
                    builder.Contracts.Register<OrderSubmittedIntegrationEvent>("orders.events.submitted", 1);
                    builder.UseProcessorOptions(new OutboxProcessorOptions
                    {
                        BatchSize = 10,
                        LeaseOwner = "trace-publisher",
                        Retry = new RetryOptions { UseJitter = false }
                    });
                    builder.UseInProcessDispatcher();
                });
            })
            .BuildServiceProvider();

        var writer = serviceProvider.GetRequiredService<IOutbox>();
        var processor = serviceProvider.GetRequiredService<IOutboxProcessor>();

        await writer.EnqueueAsync(
            new OrderSubmittedIntegrationEvent { OrderId = Guid.NewGuid() },
            new OutboxOptions
            {
                Id = Guid.NewGuid(),
                CorrelationId = "correlation-42",
                CausationId = "causation-7",
                TenantId = "tenant-west"
            });

        await processor.ProcessPendingAsync();

        capture.CorrelationId.Should().Be("correlation-42");
        capture.CausationId.Should().Be("causation-7");
        capture.TenantId.Should().Be("tenant-west");
    }

    [Fact]
    public void AddOutboxInProcessDispatcher_ShouldRegisterInProcessOutboxDispatcher()
    {
        var serviceProvider = new ServiceCollection()
            .AddLiteBus(modules =>
            {
                modules.AddEventModule();
                modules.AddOutboxModule(outbox => outbox.UseInProcessDispatcher());
            })
            .BuildServiceProvider();

        serviceProvider.GetRequiredService<IOutboxDispatcher>().Should().BeOfType<InProcessOutboxDispatcher>();
    }

    [Fact]
    public void AddOutboxInProcessDispatcher_WhenAnotherDispatcherRegistered_ShouldThrow()
    {
        var act = () => new ServiceCollection()
            .AddLiteBus(modules =>
            {
                modules.AddEventModule();
                modules.AddOutboxModule();
                modules.Register(new PreRegisteredOutboxDispatcherModule());
                modules.Register(new InProcessOutboxDispatchModule());
            })
            .BuildServiceProvider();

        act.Should().Throw<LiteBus.Runtime.Abstractions.Exceptions.LiteBusConfigurationException>()
            .WithMessage("*IOutboxDispatcher*");
    }

    [Fact]
    public void AddOutboxInProcessDispatcher_WhenCalledTwice_ShouldThrow()
    {
        var act = () =>
        {
            new ServiceCollection()
                .AddLiteBus(modules =>
                {
                    modules.AddEventModule();
                    modules.AddOutboxModule(outbox =>
                    {
                        outbox.UseInProcessDispatcher();
                        outbox.UseInProcessDispatcher();
                    });
                });
        };

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Outbox dispatcher is already configured*");
    }

    private sealed class PreRegisteredOutboxDispatcherModule : LiteBus.Runtime.Abstractions.IModule
    {
        public void Build(LiteBus.Runtime.Abstractions.IModuleConfiguration configuration)
        {
            configuration.DependencyRegistry.Register(new LiteBus.Runtime.Abstractions.DependencyDescriptor(
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
