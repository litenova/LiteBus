using LiteBus.Messaging.Abstractions.DurableMessaging;

namespace LiteBus.Runtime.UnitTests.Messaging.Abstractions;

public sealed class DurableMessagingValueObjectTests
{
    [Fact]
    public void MessageIdentity_Generated_ShouldExposeSingletonInstance()
    {
        MessageIdentity.Generated.Instance.Should().BeSameAs(MessageIdentity.Generated.Instance);
        MessageIdentity.Generated.Instance.Should().BeOfType<MessageIdentity.Generated>();
    }

    [Fact]
    public void MessageIdentity_Supplied_ShouldCarryValueAndSupportEquality()
    {
        var messageId = Guid.Parse("0192f0a8-3b2c-7d4e-8f5a-6b7c8d9e0f1a");
        var first = new MessageIdentity.Supplied(messageId);
        var second = new MessageIdentity.Supplied(messageId);

        first.Should().Be(second);
        first.Value.Should().Be(messageId);
    }

    [Fact]
    public void Idempotency_None_ShouldExposeSingletonInstance()
    {
        Idempotency.None.Instance.Should().BeSameAs(Idempotency.None.Instance);
        Idempotency.None.Instance.Should().BeOfType<Idempotency.None>();
    }

    [Fact]
    public void Idempotency_Keyed_ShouldCarryKeyAndSupportEquality()
    {
        var first = new Idempotency.Keyed("payment:42");
        var second = new Idempotency.Keyed("payment:42");

        first.Should().Be(second);
        first.Key.Should().Be("payment:42");
    }

    [Fact]
    public void MessageVisibility_Immediate_ShouldExposeSingletonInstance()
    {
        MessageVisibility.Immediate.Instance.Should().BeSameAs(MessageVisibility.Immediate.Instance);
        MessageVisibility.Immediate.Instance.Should().BeOfType<MessageVisibility.Immediate>();
    }

    [Fact]
    public void MessageVisibility_At_ShouldCarryVisibleAfterTimestamp()
    {
        var visibleAfter = new DateTimeOffset(2026, 6, 11, 12, 0, 0, TimeSpan.Zero);
        var visibility = new MessageVisibility.At(visibleAfter);

        visibility.VisibleAfter.Should().Be(visibleAfter);
    }

    [Fact]
    public void MessageVisibility_After_ShouldCarryDelay()
    {
        var delay = TimeSpan.FromMinutes(5);
        var visibility = new MessageVisibility.After(delay);

        visibility.Delay.Should().Be(delay);
    }

    [Fact]
    public void MessageTrace_None_ShouldExposeSingletonInstance()
    {
        MessageTrace.None.Instance.Should().BeSameAs(MessageTrace.None.Instance);
        MessageTrace.None.Instance.Should().BeOfType<MessageTrace.None>();
    }

    [Fact]
    public void MessageTrace_Correlated_ShouldCarryCorrelationId()
    {
        var trace = new MessageTrace.Correlated("order-9001");

        trace.CorrelationId.Should().Be("order-9001");
    }

    [Fact]
    public void MessageTrace_Workflow_ShouldCarryCorrelationAndCausationIds()
    {
        var trace = new MessageTrace.Workflow("order-9001", "payment-42");

        trace.CorrelationId.Should().Be("order-9001");
        trace.CausationId.Should().Be("payment-42");
    }

    [Fact]
    public void MessageTrace_Distributed_ShouldCarryFullTracePayload()
    {
        const string traceContext = """{"traceparent":"00-abc-def-01"}""";
        var trace = new MessageTrace.Distributed("order-9001", "payment-42", traceContext);

        trace.CorrelationId.Should().Be("order-9001");
        trace.CausationId.Should().Be("payment-42");
        trace.TraceContext.Should().Be(traceContext);
    }

    [Fact]
    public void TenantScope_Unscoped_ShouldExposeSingletonInstance()
    {
        TenantScope.Unscoped.Instance.Should().BeSameAs(TenantScope.Unscoped.Instance);
        TenantScope.Unscoped.Instance.Should().BeOfType<TenantScope.Unscoped>();
    }

    [Fact]
    public void TenantScope_Isolated_ShouldCarryTenantId()
    {
        var tenant = new TenantScope.Isolated("tenant-a");

        tenant.TenantId.Should().Be("tenant-a");
    }

    [Fact]
    public void MessageContractReference_ShouldCarryNameAndVersion()
    {
        var contract = new MessageContractReference
        {
            Name = "orders.submit",
            Version = 2
        };

        contract.Name.Should().Be("orders.submit");
        contract.Version.Should().Be(2);
    }

    [Fact]
    public void DurableMessagingVariants_ShouldSupportPatternMatching()
    {
        MessageIdentity identity = new MessageIdentity.Supplied(Guid.Empty);
        Idempotency idempotency = new Idempotency.Keyed("key");
        MessageVisibility visibility = new MessageVisibility.After(TimeSpan.FromSeconds(1));
        MessageTrace trace = new MessageTrace.Workflow("c", "x");
        TenantScope tenant = new TenantScope.Isolated("t1");

        var identityLabel = identity switch
        {
            MessageIdentity.Generated => "generated",
            MessageIdentity.Supplied  => "supplied",
            _                         => "unknown"
        };

        var idempotencyLabel = idempotency switch
        {
            Idempotency.None  => "none",
            Idempotency.Keyed => "keyed",
            _                 => "unknown"
        };

        var visibilityLabel = visibility switch
        {
            MessageVisibility.Immediate => "immediate",
            MessageVisibility.At        => "at",
            MessageVisibility.After     => "after",
            _                           => "unknown"
        };

        var traceLabel = trace switch
        {
            MessageTrace.None        => "none",
            MessageTrace.Correlated  => "correlated",
            MessageTrace.Workflow    => "workflow",
            MessageTrace.Distributed => "distributed",
            _                        => "unknown"
        };

        var tenantLabel = tenant switch
        {
            TenantScope.Unscoped => "unscoped",
            TenantScope.Isolated => "isolated",
            _                    => "unknown"
        };

        identityLabel.Should().Be("supplied");
        idempotencyLabel.Should().Be("keyed");
        visibilityLabel.Should().Be("after");
        traceLabel.Should().Be("workflow");
        tenantLabel.Should().Be("isolated");
    }
}
