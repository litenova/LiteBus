using LiteBus.Events;
using LiteBus.Events.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Dispatch.InProcess;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Outbox.UnitTests.Lineage;

/// <summary>
///     Verifies the in-process outbox dispatcher delivers every envelope as a root mediation with a cause.
/// </summary>
[Collection("Sequential")]
public sealed class OutboxDispatcherLineageTests : LiteBusTestBase
{
    [Fact]
    public async Task DispatchAsync_SetsCauseFromEnvelopeTraceFields()
    {
        var mediator = new RecordingEventMediator();
        var dispatcher = CreateDispatcher(mediator);
        var envelopeId = Guid.NewGuid();
        var payload = await SerializeAsync(new LineageProbeEvent { Value = "probe" }).ConfigureAwait(false);

        await dispatcher.DispatchAsync(CreateEnvelope(envelopeId, payload, "correlation-42", "causation-7", "tenant-west")).ConfigureAwait(false);

        mediator.Published.Should().ContainSingle();
        mediator.LastSettings.Should().NotBeNull();
        mediator.LastSettings!.Cause.Should().Be(new MediationCause("correlation-42", "causation-7", MediationCauseSource.Outbox));
        mediator.LastSettings.Identity.Should().Be(ExecutionIdentity.ForTenant("tenant-west"));
    }

    [Fact]
    public async Task DispatchAsync_WithoutEnvelopeTraceFields_UsesEnvelopeIdentifierAsCause()
    {
        var mediator = new RecordingEventMediator();
        var dispatcher = CreateDispatcher(mediator);
        var envelopeId = Guid.NewGuid();
        var payload = await SerializeAsync(new LineageProbeEvent { Value = "probe" }).ConfigureAwait(false);

        await dispatcher.DispatchAsync(CreateEnvelope(envelopeId, payload, null, null, null)).ConfigureAwait(false);

        var fallback = envelopeId.ToString("D");
        mediator.LastSettings.Should().NotBeNull();
        mediator.LastSettings!.Cause.Should().Be(new MediationCause(fallback, fallback, MediationCauseSource.Outbox));
    }

    [Fact]
    public async Task DispatchAsync_WithPartialEnvelopeTraceFields_FallsBackToEnvelopeIdentifier()
    {
        var mediator = new RecordingEventMediator();
        var dispatcher = CreateDispatcher(mediator);
        var envelopeId = Guid.NewGuid();
        var payload = await SerializeAsync(new LineageProbeEvent { Value = "probe" }).ConfigureAwait(false);

        await dispatcher.DispatchAsync(CreateEnvelope(envelopeId, payload, "correlation-42", null, null)).ConfigureAwait(false);

        mediator.LastSettings.Should().NotBeNull();
        mediator.LastSettings!.Cause.Should().Be(new MediationCause("correlation-42", envelopeId.ToString("D"), MediationCauseSource.Outbox));
    }

    [Fact]
    public async Task DispatchAsync_InsideAMediation_DeliversAsRootMediation()
    {
        var lineage = new LineageCapture();
        var fixture = new NestedDispatchFixture();
        var serializer = new SystemTextJsonMessageSerializer();
        var payload = await serializer.SerializeAsync(new LineageProbeEvent { Value = "inner" }).ConfigureAwait(false);

        fixture.InnerEnvelope = CreateEnvelope(Guid.NewGuid(), payload, "correlation-42", "causation-7", "tenant-west");

        var provider = new ServiceCollection()
            .AddSingleton(lineage)
            .AddSingleton(fixture)
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ =>
                {
                });

                registry.AddEvents(builder =>
                {
                    builder.Register<OuterLineageEventHandler>();
                    builder.Register<InnerLineageEventHandler>();
                });
            })
            .BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            var contracts = new MessageContractRegistry();
            contracts.Register<LineageProbeEvent>("lineage.events.probe");
            fixture.Dispatcher = new EventOutboxDispatcher(
                provider.GetRequiredService<IEventMediator>(),
                contracts,
                serializer);

            await provider.GetRequiredService<IEventMediator>().PublishAsync(new OuterLineageEvent()).ConfigureAwait(false);

            lineage.InnerParent.Should().BeNull();
            lineage.InnerCause.Should().Be(new MediationCause("correlation-42", "causation-7", MediationCauseSource.Outbox));
            lineage.OuterParent.Should().BeNull();
            lineage.OuterCause.Should().BeNull();
            lineage.AmbientRestored.Should().BeTrue();
        }
    }

    private static EventOutboxDispatcher CreateDispatcher(RecordingEventMediator mediator)
    {
        var contracts = new MessageContractRegistry();
        contracts.Register<LineageProbeEvent>("lineage.events.probe");

        return new EventOutboxDispatcher(mediator, contracts, new SystemTextJsonMessageSerializer());
    }

    private static Task<string> SerializeAsync(LineageProbeEvent @event)
    {
        return new SystemTextJsonMessageSerializer().SerializeAsync(@event);
    }

    private static OutboxEnvelope CreateEnvelope(
        Guid id,
        string payload,
        string? correlationId,
        string? causationId,
        string? tenantId)
    {
        return new OutboxEnvelope
        {
            Id = id,
            ContractName = "lineage.events.probe",
            ContractVersion = 1,
            Payload = payload,
            CreatedAt = DateTimeOffset.UtcNow,
            AttemptCount = 0,
            Status = OutboxStatus.Pending,
            CorrelationId = correlationId,
            CausationId = causationId,
            TenantId = tenantId
        };
    }

    public sealed record LineageProbeEvent
    {
        public required string Value { get; init; }
    }

    public sealed record OuterLineageEvent;

    public sealed class RecordingEventMediator : IEventMediator
    {
        private readonly List<object> _published = [];

        public IReadOnlyList<object> Published => _published;

        public EventMediationSettings? LastSettings { get; private set; }

        public Task PublishAsync(IEvent @event, EventMediationSettings? eventMediationSettings = null, CancellationToken cancellationToken = default)
        {
            _published.Add(@event);
            LastSettings = eventMediationSettings;
            return Task.CompletedTask;
        }

        public Task PublishAsync<TEvent>(TEvent @event, EventMediationSettings? eventMediationSettings = null, CancellationToken cancellationToken = default)
            where TEvent : notnull
        {
            _published.Add(@event);
            LastSettings = eventMediationSettings;
            return Task.CompletedTask;
        }
    }

    public sealed class NestedDispatchFixture
    {
        public OutboxEnvelope InnerEnvelope { get; set; } = null!;

        public EventOutboxDispatcher Dispatcher { get; set; } = null!;
    }

    public sealed class LineageCapture
    {
        public IExecutionContext? InnerParent { get; set; }

        public MediationCause? InnerCause { get; set; }

        public IExecutionContext? OuterParent { get; set; }

        public MediationCause? OuterCause { get; set; }

        public bool AmbientRestored { get; set; }
    }

    public sealed class OuterLineageEventHandler : IEventHandler<OuterLineageEvent>
    {
        private readonly NestedDispatchFixture _fixture;
        private readonly LineageCapture _capture;

        public OuterLineageEventHandler(NestedDispatchFixture fixture, LineageCapture capture)
        {
            _fixture = fixture;
            _capture = capture;
        }

        public async Task HandleAsync(OuterLineageEvent message, CancellationToken cancellationToken = default)
        {
            var ambientBefore = AmbientExecutionContext.Current;
            _capture.OuterParent = ambientBefore.Parent;
            _capture.OuterCause = ambientBefore.Cause;

            await _fixture.Dispatcher.DispatchAsync(_fixture.InnerEnvelope, cancellationToken).ConfigureAwait(false);

            _capture.AmbientRestored = ReferenceEquals(AmbientExecutionContext.Current, ambientBefore);
        }
    }

    public sealed class InnerLineageEventHandler : IEventHandler<LineageProbeEvent>
    {
        private readonly LineageCapture _capture;

        public InnerLineageEventHandler(LineageCapture capture)
        {
            _capture = capture;
        }

        public Task HandleAsync(LineageProbeEvent message, CancellationToken cancellationToken = default)
        {
            var current = AmbientExecutionContext.Current;
            _capture.InnerParent = current.Parent;
            _capture.InnerCause = current.Cause;
            return Task.CompletedTask;
        }
    }
}
