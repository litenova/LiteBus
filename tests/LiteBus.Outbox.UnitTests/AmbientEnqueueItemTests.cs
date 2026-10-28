using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox.Abstractions;

namespace LiteBus.Outbox.UnitTests;

/// <summary>
///     Verifies <c>OutboxEnqueueItem.FromAmbient</c> stamps trace metadata from the ambient execution context.
/// </summary>
[Collection("Sequential")]
public sealed class AmbientEnqueueItemTests
{
    /// <summary>
    ///     Verifies a processor-built scope stamps the distributed trace and leaves every other concern immediate.
    /// </summary>
    [Fact]
    public void FromAmbient_inside_a_processor_built_scope_stamps_the_distributed_trace()
    {
        var messageId = Guid.NewGuid();
        var items = new Dictionary<string, object>();

        MessageProcessorDiagnostics.ApplyTraceMetadata(
            items,
            "correlation-42",
            "causation-7",
            "tenant-west",
            """{"traceparent":"00-abc-def-01"}""");

        // This literal mirrors InboxExecutionContextKeys.MessageId. This project does not reference
        // LiteBus.Inbox.Abstractions, and the inbox ambient tests pin the same read through the constant.
        items["__LiteBus.Inbox.MessageId"] = messageId;

        using (AmbientExecutionContext.CreateScope(new TestExecutionContext(items)))
        {
            var message = new TestEvent();
            var item = OutboxEnqueueItem<TestEvent>.FromAmbient(message);

            item.Message.Should().BeSameAs(message);
            item.Metadata.Trace.Should().Be(new MessageTrace.Distributed(
                "correlation-42",
                $"{messageId}",
                """{"traceparent":"00-abc-def-01"}"""));
            item.Metadata.Tenant.Should().Be(TenantScope.Unscoped.Instance);
            item.Metadata.Should().Be(OutboxEnqueueMetadata.Immediate with { Trace = item.Metadata.Trace });
        }
    }

    /// <summary>
    ///     Verifies ambient correlation and an incoming message id stamp a workflow trace when no trace context exists.
    /// </summary>
    [Fact]
    public void FromAmbient_without_a_distributed_context_stamps_a_workflow_trace()
    {
        var messageId = Guid.NewGuid();
        var items = new Dictionary<string, object>();

        MessageProcessorDiagnostics.ApplyTraceMetadata(items, "correlation-42", "causation-7", null);
        items["__LiteBus.Inbox.MessageId"] = messageId;

        using (AmbientExecutionContext.CreateScope(new TestExecutionContext(items)))
        {
            var item = OutboxEnqueueItem<TestEvent>.FromAmbient(new TestEvent());

            item.Metadata.Trace.Should().Be(new MessageTrace.Workflow("correlation-42", $"{messageId}"));
        }
    }

    /// <summary>
    ///     Verifies the ambient causation id is preserved when no incoming message id is stored, as in outbox replay.
    /// </summary>
    [Fact]
    public void FromAmbient_without_an_incoming_message_id_preserves_the_ambient_causation()
    {
        var items = new Dictionary<string, object>();

        MessageProcessorDiagnostics.ApplyTraceMetadata(items, "correlation-42", "causation-7", null, "trace-context");

        using (AmbientExecutionContext.CreateScope(new TestExecutionContext(items)))
        {
            var item = OutboxEnqueueItem<TestEvent>.FromAmbient(new TestEvent());

            item.Metadata.Trace.Should().Be(new MessageTrace.Distributed("correlation-42", "causation-7", "trace-context"));
        }
    }

    /// <summary>
    ///     Verifies calling <c>FromAmbient</c> outside any scope yields metadata identical to <c>From</c>.
    /// </summary>
    [Fact]
    public void FromAmbient_outside_any_scope_yields_immediate_metadata()
    {
        AmbientExecutionContext.ResetForTesting();

        var message = new TestEvent();
        var item = OutboxEnqueueItem<TestEvent>.FromAmbient(message);

        item.Message.Should().BeSameAs(message);
        item.Metadata.Should().Be(OutboxEnqueueMetadata.Immediate);
    }

    /// <summary>
    ///     Verifies an ambient scope carrying no trace keys yields metadata identical to <c>From</c>.
    /// </summary>
    [Fact]
    public void FromAmbient_inside_an_empty_scope_yields_immediate_metadata()
    {
        using (AmbientExecutionContext.CreateScope(new TestExecutionContext(new Dictionary<string, object>())))
        {
            var item = OutboxEnqueueItem<TestEvent>.FromAmbient(new TestEvent());

            item.Metadata.Should().Be(OutboxEnqueueMetadata.Immediate);
        }
    }

    private sealed record TestEvent;

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
