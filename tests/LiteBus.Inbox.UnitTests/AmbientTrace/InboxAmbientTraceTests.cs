using LiteBus.Commands;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Dispatch.InProcess;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Inbox.UnitTests.AmbientTrace;

/// <summary>
///     Verifies <see cref="InboxModuleBuilder.UseAmbientTrace" /> propagates the ambient trace onto untraced accepts.
/// </summary>
[Collection("Sequential")]
public sealed class InboxAmbientTraceTests
{
    /// <summary>
    ///     Verifies the ambient trace opt-in is disabled unless the module builder enables it.
    /// </summary>
    [Fact]
    public void Ambient_trace_is_disabled_by_default()
    {
        new InboxModuleBuilder().IsAmbientTraceEnabled.Should().BeFalse();

        new InboxModuleBuilder().UseAmbientTrace().IsAmbientTraceEnabled.Should().BeTrue();
    }

    /// <summary>
    ///     Verifies a factory built without the opt-in ignores ambient state and leaves the envelope untraced.
    /// </summary>
    [Fact]
    public async Task Untraced_accept_without_the_opt_in_ignores_the_ambient_trace()
    {
        var factory = CreateFactory(useAmbientTrace: false);
        var messageId = Guid.NewGuid();

        using (AmbientExecutionContext.CreateScope(new TestExecutionContext(CreateAmbientItems(messageId))))
        {
            var envelope = await factory.CreateAsync(CreateItem()).ConfigureAwait(false);

            envelope.CorrelationId.Should().BeNull();
            envelope.CausationId.Should().BeNull();
            envelope.TraceContext.Should().BeNull();
        }
    }

    /// <summary>
    ///     Verifies an untraced accept with the opt-in inherits correlation, causation, and trace context from the ambient scope.
    /// </summary>
    [Fact]
    public async Task Untraced_accept_with_the_opt_in_inherits_the_ambient_trace()
    {
        var factory = CreateFactory(useAmbientTrace: true);
        var messageId = Guid.NewGuid();

        using (AmbientExecutionContext.CreateScope(new TestExecutionContext(CreateAmbientItems(messageId))))
        {
            var envelope = await factory.CreateAsync(CreateItem()).ConfigureAwait(false);

            envelope.CorrelationId.Should().Be("correlation-42");
            envelope.CausationId.Should().Be($"{messageId}");
            envelope.TraceContext.Should().Be("trace-context");
            envelope.TenantId.Should().BeNull();
        }
    }

    /// <summary>
    ///     Verifies an explicit trace is kept when the opt-in is enabled.
    /// </summary>
    [Fact]
    public async Task Explicit_trace_is_kept_when_the_opt_in_is_enabled()
    {
        var factory = CreateFactory(useAmbientTrace: true);
        var messageId = Guid.NewGuid();
        var metadata = InboxAcceptMetadata.Immediate with
        {
            Trace = new MessageTrace.Correlated("explicit-correlation")
        };

        using (AmbientExecutionContext.CreateScope(new TestExecutionContext(CreateAmbientItems(messageId))))
        {
            var envelope = await factory.CreateAsync(CreateItem(metadata)).ConfigureAwait(false);

            envelope.CorrelationId.Should().Be("explicit-correlation");
            envelope.CausationId.Should().BeNull();
            envelope.TraceContext.Should().BeNull();
        }
    }

    /// <summary>
    ///     Verifies the opt-in leaves envelopes untraced outside any mediation.
    /// </summary>
    [Fact]
    public async Task Opt_in_outside_a_mediation_leaves_the_envelope_untraced()
    {
        AmbientExecutionContext.ResetForTesting();
        var factory = CreateFactory(useAmbientTrace: true);

        var envelope = await factory.CreateAsync(CreateItem()).ConfigureAwait(false);

        envelope.CorrelationId.Should().BeNull();
        envelope.CausationId.Should().BeNull();
        envelope.TraceContext.Should().BeNull();
    }

    /// <summary>
    ///     Verifies the builder flag flows through module composition into envelopes accepted through the composed writer.
    /// </summary>
    [Fact]
    public async Task UseAmbientTrace_flows_from_the_module_builder_to_accepted_envelopes()
    {
        var messageId = Guid.NewGuid();
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
                    inbox.UseAmbientTrace();
                    inbox.UseInMemoryStorage();
                    inbox.UseInProcessDispatch();
                });
            })
            .BuildServiceProvider();

        var inboxWriter = provider.GetRequiredService<IInbox>();
        var store = provider.GetRequiredService<InMemoryInboxStore>();

        using (AmbientExecutionContext.CreateScope(new TestExecutionContext(CreateAmbientItems(messageId))))
        {
            var receipt = await inboxWriter.AcceptAsync(new InboxTestFixtures.ShipOrderCommand
            {
                OrderId = Guid.NewGuid(),
                IdempotencyKey = $"ambient:{messageId}"
            }).ConfigureAwait(false);

            var envelope = store.Get(receipt.Id);
            envelope.CorrelationId.Should().Be("correlation-42");
            envelope.CausationId.Should().Be($"{messageId}");
            envelope.TraceContext.Should().Be("trace-context");
        }
    }

    private static InboxEnvelopeFactory CreateFactory(bool useAmbientTrace)
    {
        var registry = new MessageContractRegistry();
        registry.Register<InboxTestFixtures.ShipOrderCommand>("orders.commands.ship");

        InboxEnvelopeOptions? options = useAmbientTrace
            ? new InboxEnvelopeOptions { UseAmbientTrace = true }
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

    private static Dictionary<string, object> CreateAmbientItems(Guid incomingMessageId)
    {
        var items = new Dictionary<string, object>();
        MessageProcessorDiagnostics.ApplyTraceMetadata(items, "correlation-42", "causation-7", "tenant-west", "trace-context");
        items[InboxExecutionContextKeys.MessageId] = incomingMessageId;
        return items;
    }

    private sealed class TestExecutionContext : IExecutionContext
    {
        public TestExecutionContext(IDictionary<string, object> items)
        {
            Items = items;
        }

        public CancellationToken CancellationToken => CancellationToken.None;

        public IDictionary<string, object> Items { get; }

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
