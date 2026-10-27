using LiteBus.Inbox.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Inbox.UnitTests.Abstractions;

/// <summary>
///     Verifies <c>InboxAcceptItem.FromAmbient</c> stamps trace metadata from the ambient execution context.
/// </summary>
[Collection("Sequential")]
public sealed class AmbientAcceptItemTests
{
    /// <summary>
    ///     Verifies a processor-built scope stamps the distributed trace and leaves every other concern immediate.
    /// </summary>
    [Fact]
    public void FromAmbient_inside_a_processor_built_scope_stamps_the_distributed_trace()
    {
        var messageId = Guid.NewGuid();
        var items = new Dictionary<string, object>();

        MessageProcessorDiagnostics.ApplyTraceMetadata(items, "correlation-42", "causation-7", "tenant-west", "trace-context");
        items[InboxExecutionContextKeys.MessageId] = messageId;

        using (AmbientExecutionContext.CreateScope(new TestExecutionContext(items)))
        {
            var message = "payload";
            var item = InboxAcceptItem<string>.FromAmbient(message);

            item.Message.Should().BeSameAs(message);
            item.Metadata.Trace.Should().Be(new MessageTrace.Distributed("correlation-42", $"{messageId}", "trace-context"));
            item.Metadata.Tenant.Should().Be(TenantScope.Unscoped.Instance);
            item.Metadata.Should().Be(InboxAcceptMetadata.Immediate with { Trace = item.Metadata.Trace });
        }
    }

    /// <summary>
    ///     Verifies the incoming message id doubles as the correlation id when the ambient correlation id is absent.
    /// </summary>
    [Fact]
    public void FromAmbient_without_an_ambient_correlation_id_falls_back_to_the_incoming_message_id()
    {
        var messageId = Guid.NewGuid();
        var items = new Dictionary<string, object>();
        items[InboxExecutionContextKeys.MessageId] = messageId;

        using (AmbientExecutionContext.CreateScope(new TestExecutionContext(items)))
        {
            var item = InboxAcceptItem<string>.FromAmbient("payload");

            item.Metadata.Trace.Should().Be(new MessageTrace.Workflow($"{messageId}", $"{messageId}"));
        }
    }

    /// <summary>
    ///     Verifies calling <c>FromAmbient</c> outside any scope yields metadata identical to <c>From</c>.
    /// </summary>
    [Fact]
    public void FromAmbient_outside_any_scope_yields_immediate_metadata()
    {
        AmbientExecutionContext.ResetForTesting();

        var message = "payload";
        var item = InboxAcceptItem<string>.FromAmbient(message);

        item.Message.Should().BeSameAs(message);
        item.Metadata.Should().Be(InboxAcceptMetadata.Immediate);
    }

    /// <summary>
    ///     Verifies an ambient scope carrying no trace keys yields metadata identical to <c>From</c>.
    /// </summary>
    [Fact]
    public void FromAmbient_inside_an_empty_scope_yields_immediate_metadata()
    {
        using (AmbientExecutionContext.CreateScope(new TestExecutionContext(new Dictionary<string, object>())))
        {
            var item = InboxAcceptItem<string>.FromAmbient("payload");

            item.Metadata.Should().Be(InboxAcceptMetadata.Immediate);
        }
    }

    private sealed class TestExecutionContext : IExecutionContext
    {
        public TestExecutionContext(IDictionary<string, object> items)
        {
            Items = items;
        }

        public CancellationToken CancellationToken => CancellationToken.None;

        public DateTimeOffset HandledAt { get; } = DateTimeOffset.UtcNow;

        public IDictionary<string, object> Items { get; }

        public IHandleContextData Data { get; } = new HandleContextData();

        public IReadOnlyCollection<string> Tags { get; } = [];

        public object? MessageResult { get; set; }

        public bool PostHandlersSuppressed { get; private set; }

        public void SuppressPostHandlers()
        {
            PostHandlersSuppressed = true;
        }
    }
}
