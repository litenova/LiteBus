using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Dispatch.InProcess;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Inbox.UnitTests.AmbientTenant;

/// <summary>
///     Verifies <see cref="InboxModuleBuilder.UseAmbientTenant" /> stamps the ambient tenant onto unscoped accepts,
///     and that the in-process dispatcher restores the envelope tenant into the mediation identity.
/// </summary>
[Collection("Sequential")]
public sealed class InboxAmbientTenantTests
{
    /// <summary>
    ///     Verifies the ambient tenant opt-in is disabled unless the module builder enables it.
    /// </summary>
    [Fact]
    public void Ambient_tenant_is_disabled_by_default()
    {
        new InboxModuleBuilder().IsAmbientTenantEnabled.Should().BeFalse();

        new InboxModuleBuilder().UseAmbientTenant().IsAmbientTenantEnabled.Should().BeTrue();

        new InboxModuleBuilder().UseAmbientTrace().IsAmbientTenantEnabled.Should().BeFalse();
    }

    /// <summary>
    ///     Verifies a factory built without the opt-in ignores ambient state and leaves the envelope unscoped.
    /// </summary>
    [Fact]
    public async Task Unscoped_accept_without_the_opt_in_ignores_the_ambient_tenant()
    {
        var factory = CreateFactory(useAmbientTenant: false);

        using (AmbientExecutionContext.CreateScope(new TestExecutionContext(
                   CreateAmbientItems(),
                   ExecutionIdentity.ForTenant("tenant-east"))))
        {
            var envelope = await factory.CreateAsync(CreateItem()).ConfigureAwait(false);

            envelope.TenantId.Should().BeNull();
        }
    }

    /// <summary>
    ///     Verifies an unscoped accept with the opt-in inherits the tenant of the ambient execution identity.
    /// </summary>
    [Fact]
    public async Task Unscoped_accept_with_the_opt_in_inherits_the_ambient_tenant()
    {
        var factory = CreateFactory(useAmbientTenant: true);

        using (AmbientExecutionContext.CreateScope(new TestExecutionContext(
                   CreateAmbientItems(),
                   ExecutionIdentity.ForTenant("tenant-east"))))
        {
            var envelope = await factory.CreateAsync(CreateItem()).ConfigureAwait(false);

            envelope.TenantId.Should().Be("tenant-east");
            envelope.CorrelationId.Should().BeNull();
        }
    }

    /// <summary>
    ///     Verifies an explicit tenant is kept when the opt-in is enabled.
    /// </summary>
    [Fact]
    public async Task Explicit_tenant_is_kept_when_the_opt_in_is_enabled()
    {
        var factory = CreateFactory(useAmbientTenant: true);
        var metadata = InboxAcceptMetadata.Immediate with
        {
            Tenant = new TenantScope.Isolated("tenant-explicit")
        };

        using (AmbientExecutionContext.CreateScope(new TestExecutionContext(
                   CreateAmbientItems(),
                   ExecutionIdentity.ForTenant("tenant-east"))))
        {
            var envelope = await factory.CreateAsync(CreateItem(metadata)).ConfigureAwait(false);

            envelope.TenantId.Should().Be("tenant-explicit");
        }
    }

    /// <summary>
    ///     Verifies the opt-in leaves envelopes unscoped outside any mediation.
    /// </summary>
    [Fact]
    public async Task Opt_in_outside_a_mediation_leaves_the_envelope_unscoped()
    {
        AmbientExecutionContext.ResetForTesting();
        var factory = CreateFactory(useAmbientTenant: true);

        var envelope = await factory.CreateAsync(CreateItem()).ConfigureAwait(false);

        envelope.TenantId.Should().BeNull();
    }

    /// <summary>
    ///     Verifies the builder flag flows through module composition into envelopes accepted through the composed writer.
    /// </summary>
    [Fact]
    public async Task UseAmbientTenant_flows_from_the_module_builder_to_accepted_envelopes()
    {
        using var provider = new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ =>
                {
                });

                registry.AddCommands(builder =>
                {
                    builder.Register<InboxTestFixtures.ShipOrderCommand>();
                    builder.Register<InboxTestFixtures.ShipOrderCommandHandler>();
                });

                registry.AddInbox(inbox =>
                {
                    inbox.Contracts.Register<InboxTestFixtures.ShipOrderCommand>("orders.commands.ship");
                    inbox.UseAmbientTenant();
                    inbox.UseInMemoryStorage();
                    inbox.UseInProcessDispatch();
                });
            })
            .BuildServiceProvider();

        var inboxWriter = provider.GetRequiredService<IInbox>();
        var store = provider.GetRequiredService<InMemoryInboxStore>();

        using (AmbientExecutionContext.CreateScope(new TestExecutionContext(
                   new Dictionary<string, object>(),
                   ExecutionIdentity.ForTenant("tenant-east"))))
        {
            var receipt = await inboxWriter.AcceptAsync(new InboxTestFixtures.ShipOrderCommand
            {
                OrderId = Guid.NewGuid(),
                IdempotencyKey = $"ambient:{Guid.NewGuid()}"
            }).ConfigureAwait(false);

            var envelope = store.Get(receipt.Id);
            envelope.TenantId.Should().Be("tenant-east");
        }
    }

    /// <summary>
    ///     Verifies dispatch restores the envelope tenant into the mediation identity observed by the handler.
    /// </summary>
    [Fact]
    public async Task Dispatch_restores_the_envelope_tenant_into_the_mediation_identity()
    {
        var identityCapture = new IdentityCapture();
        using var provider = new ServiceCollection()
            .AddSingleton(identityCapture)
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ =>
                {
                });

                registry.AddCommands(builder =>
                {
                    builder.Register<TenantProbeCommandHandler>();
                });

                registry.AddInbox(inbox =>
                {
                    inbox.Contracts.Register<TenantProbeCommand>("tests.commands.tenant-probe");
                    inbox.UseInMemoryStorage();
                    inbox.UseInProcessDispatch();
                });
            })
            .BuildServiceProvider();

        var serializer = new SystemTextJsonMessageSerializer();
        var payload = await serializer.SerializeAsync(new TenantProbeCommand()).ConfigureAwait(false);
        var envelope = CreateEnvelope(payload, "tenant-west");

        var dispatcher = provider.GetRequiredService<IInboxDispatcher>();
        await dispatcher.DispatchAsync(envelope).ConfigureAwait(false);

        identityCapture.Identity.Should().Be(ExecutionIdentity.ForTenant("tenant-west"));
    }

    /// <summary>
    ///     Verifies dispatch of an unscoped envelope runs without a tenant.
    /// </summary>
    [Fact]
    public async Task Dispatch_of_an_unscoped_envelope_runs_without_a_tenant()
    {
        var identityCapture = new IdentityCapture();
        using var provider = new ServiceCollection()
            .AddSingleton(identityCapture)
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ =>
                {
                });

                registry.AddCommands(builder =>
                {
                    builder.Register<TenantProbeCommandHandler>();
                });

                registry.AddInbox(inbox =>
                {
                    inbox.Contracts.Register<TenantProbeCommand>("tests.commands.tenant-probe");
                    inbox.UseInMemoryStorage();
                    inbox.UseInProcessDispatch();
                });
            })
            .BuildServiceProvider();

        var serializer = new SystemTextJsonMessageSerializer();
        var payload = await serializer.SerializeAsync(new TenantProbeCommand()).ConfigureAwait(false);
        var envelope = CreateEnvelope(payload, null);

        var dispatcher = provider.GetRequiredService<IInboxDispatcher>();
        await dispatcher.DispatchAsync(envelope).ConfigureAwait(false);

        identityCapture.Identity.Should().Be(ExecutionIdentity.Anonymous);
    }

    private static InboxEnvelopeFactory CreateFactory(bool useAmbientTenant)
    {
        var registry = new MessageContractRegistry();
        registry.Register<InboxTestFixtures.ShipOrderCommand>("orders.commands.ship");

        InboxEnvelopeOptions? options = useAmbientTenant
            ? new InboxEnvelopeOptions { UseAmbientTenant = true }
            : null;

        return new InboxEnvelopeFactory(
            registry,
            new SystemTextJsonMessageSerializer(),
            TimeProvider.System,
            envelopeOptions: options);
    }

    private static InboxAcceptItem CreateItem(InboxAcceptMetadata? metadata = null)
    {
        return InboxAcceptItem.From(InboxAcceptItem<InboxTestFixtures.ShipOrderCommand>.From(
            new InboxTestFixtures.ShipOrderCommand
            {
                OrderId = Guid.NewGuid(),
                IdempotencyKey = $"ambient:{Guid.NewGuid()}"
            },
            metadata));
    }

    private static Dictionary<string, object> CreateAmbientItems()
    {
        Dictionary<string, object> items = [];
        MessageProcessorDiagnostics.ApplyTraceMetadata(items, "correlation-42", null, "tenant-from-trace", null);
        return items;
    }

    private static InboxEnvelope CreateEnvelope(string payload, string? tenantId)
    {
        return new InboxEnvelope
        {
            Id = Guid.NewGuid(),
            ContractName = "tests.commands.tenant-probe",
            ContractVersion = 1,
            Payload = payload,
            CreatedAt = DateTimeOffset.UtcNow,
            AttemptCount = 0,
            Status = InboxStatus.Pending,
            TenantId = tenantId
        };
    }

    /// <summary>
    ///     Captures the execution identity observed by the probe handler during dispatch.
    /// </summary>
    public sealed class IdentityCapture
    {
        /// <summary>
        ///     Gets or sets the identity observed by the probe handler.
        /// </summary>
        public ExecutionIdentity? Identity { get; set; }
    }

    /// <summary>
    ///     Probe command dispatched to verify tenant restoration.
    /// </summary>
    public sealed record TenantProbeCommand : ICommand;

    /// <summary>
    ///     Records the ambient execution identity of each dispatched probe command.
    /// </summary>
    public sealed class TenantProbeCommandHandler : ICommandHandler<TenantProbeCommand>
    {
        /// <summary>
        ///     Gets the capture receiving the observed identity.
        /// </summary>
        private readonly IdentityCapture _capture;

        /// <summary>
        ///     Initializes a new instance of the <see cref="TenantProbeCommandHandler" /> class.
        /// </summary>
        /// <param name="capture">The capture receiving the observed identity.</param>
        public TenantProbeCommandHandler(IdentityCapture capture)
        {
            _capture = capture;
        }

        /// <inheritdoc />
        public Task HandleAsync(TenantProbeCommand message, CancellationToken cancellationToken = default)
        {
            _capture.Identity = AmbientExecutionContext.Current.Identity;
            return Task.CompletedTask;
        }
    }

    private sealed class TestExecutionContext : IExecutionContext
    {
        public TestExecutionContext(IDictionary<string, object> items, ExecutionIdentity identity)
        {
            Items = items;
            Identity = identity;
        }

        public CancellationToken CancellationToken => CancellationToken.None;

        public IDictionary<string, object> Items { get; }

        public ExecutionIdentity Identity { get; }

        public IHandleContextData Data { get; } = new HandleContextData();

        public IReadOnlyCollection<string> Tags { get; } = [];

        public object? MessageResult { get; set; }

        public bool PostHandlersSuppressed { get; private set; }

        public DateTimeOffset HandledAt { get; set; } = DateTimeOffset.UtcNow;

        public void SuppressPostHandlers()
        {
            PostHandlersSuppressed = true;
        }
    }
}
