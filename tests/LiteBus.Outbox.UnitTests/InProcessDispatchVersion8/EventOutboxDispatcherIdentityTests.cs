using LiteBus.Events.Abstractions;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Abstractions.Exceptions;
using LiteBus.Outbox.Dispatch.InProcess;

namespace LiteBus.Outbox.UnitTests.InProcessDispatchVersion8;

/// <summary>
///     Verifies the in-process outbox dispatcher restores the envelope tenant on the mediation settings.
/// </summary>
public sealed class EventOutboxDispatcherIdentityTests
{
    private const string ContractName = "tests.events.tenant-identity";

    private const string PocoContractName = "tests.events.tenant-poco";

    /// <summary>
    ///     Verifies a tenant envelope mediates in that tenant without a principal.
    /// </summary>
    [Fact]
    public async Task DispatchAsync_with_tenant_envelope_should_set_tenant_identity()
    {
        var mediator = new CapturingEventMediator();
        var contracts = new MessageContractRegistry();
        contracts.Register<TenantTestEvent>(ContractName);
        var serializer = new SystemTextJsonMessageSerializer();
        var dispatcher = new EventOutboxDispatcher(mediator, contracts, serializer);
        var envelope = await CreateEnvelopeAsync(serializer, "tenant-west").ConfigureAwait(false);

        await dispatcher.DispatchAsync(envelope).ConfigureAwait(false);

        mediator.CapturedSettings.Should().NotBeNull();
        var identity = mediator.CapturedSettings!.Identity;
        identity.Should().NotBeNull();
        identity!.TenantId.Should().Be("tenant-west");
        identity.HasTenant.Should().BeTrue();
        identity.Principal.Should().BeNull();
    }

    /// <summary>
    ///     Verifies an unscoped envelope mediates with the anonymous identity.
    /// </summary>
    [Fact]
    public async Task DispatchAsync_without_tenant_should_set_anonymous_identity()
    {
        var mediator = new CapturingEventMediator();
        var contracts = new MessageContractRegistry();
        contracts.Register<TenantTestEvent>(ContractName);
        var serializer = new SystemTextJsonMessageSerializer();
        var dispatcher = new EventOutboxDispatcher(mediator, contracts, serializer);
        var envelope = await CreateEnvelopeAsync(serializer, null).ConfigureAwait(false);

        await dispatcher.DispatchAsync(envelope).ConfigureAwait(false);

        mediator.CapturedSettings.Should().NotBeNull();
        mediator.CapturedSettings!.Identity.Should().Be(ExecutionIdentity.Anonymous);
    }

    /// <summary>
    ///     Verifies a POCO event envelope also carries the tenant identity.
    /// </summary>
    [Fact]
    public async Task DispatchAsync_with_poco_tenant_envelope_should_set_tenant_identity()
    {
        var mediator = new CapturingEventMediator();
        var contracts = new MessageContractRegistry();
        contracts.Register<TenantPocoEvent>(PocoContractName);
        var serializer = new SystemTextJsonMessageSerializer();
        var dispatcher = new EventOutboxDispatcher(mediator, contracts, serializer);
        var payload = await serializer.SerializeAsync(new TenantPocoEvent { Value = "tenant-poco" }).ConfigureAwait(false);
        var envelope = new OutboxEnvelope
        {
            Id = Guid.NewGuid(),
            ContractName = PocoContractName,
            ContractVersion = 1,
            Payload = payload,
            CreatedAt = DateTimeOffset.UtcNow,
            Status = OutboxStatus.Publishing,
            AttemptCount = 1,
            TenantId = "tenant-east"
        };

        await dispatcher.DispatchAsync(envelope).ConfigureAwait(false);

        mediator.CapturedSettings.Should().NotBeNull();
        var identity = mediator.CapturedSettings!.Identity;
        identity.Should().NotBeNull();
        identity!.TenantId.Should().Be("tenant-east");
        identity.Principal.Should().BeNull();
    }

    /// <summary>
    ///     Verifies registration guidance names the acknowledgement method.
    /// </summary>
    [Fact]
    public async Task DispatchAsync_when_contract_is_unregistered_should_name_allow_method()
    {
        var dispatcher = new EventOutboxDispatcher(
            new CapturingEventMediator(),
            new MessageContractRegistry(),
            new SystemTextJsonMessageSerializer());

        var act = () => dispatcher.DispatchAsync(new OutboxEnvelope
        {
            Id = Guid.NewGuid(),
            ContractName = "tests.events.unknown",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
            Status = OutboxStatus.Publishing,
            AttemptCount = 1
        });

        var failure = await act.Should().ThrowAsync<LiteBusDispatchException>().ConfigureAwait(false);
        failure.Which.Message.Should().Contain("AllowEnvelopesWithoutHandler");
    }

    private static async Task<OutboxEnvelope> CreateEnvelopeAsync(SystemTextJsonMessageSerializer serializer, string? tenantId)
    {
        var payload = await serializer.SerializeAsync(new TenantTestEvent("order-1")).ConfigureAwait(false);

        return new OutboxEnvelope
        {
            Id = Guid.NewGuid(),
            ContractName = ContractName,
            ContractVersion = 1,
            Payload = payload,
            CreatedAt = DateTimeOffset.UtcNow,
            Status = OutboxStatus.Publishing,
            AttemptCount = 1,
            TenantId = tenantId
        };
    }

    private sealed record TenantTestEvent(string OrderId) : IEvent;

    private sealed record TenantPocoEvent
    {
        public required string Value { get; init; }
    }

    private sealed class CapturingEventMediator : IEventMediator
    {
        public EventMediationSettings? CapturedSettings { get; private set; }

        public Task PublishAsync(
            IEvent @event,
            EventMediationSettings? eventMediationSettings = null,
            CancellationToken cancellationToken = default)
        {
            CapturedSettings = eventMediationSettings;
            return Task.CompletedTask;
        }

        public Task PublishAsync<TEvent>(
            TEvent @event,
            EventMediationSettings? eventMediationSettings = null,
            CancellationToken cancellationToken = default)
            where TEvent : notnull
        {
            CapturedSettings = eventMediationSettings;
            return Task.CompletedTask;
        }
    }
}
