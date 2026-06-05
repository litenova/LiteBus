using System.Diagnostics;
using LiteBus.Inbox.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Inbox.UnitTests;

/// <summary>
///     Verifies W3C trace context parsing used when inbox processor activities start.
/// </summary>
public sealed class MessageProcessorDiagnosticsTraceContextTests
{
    /// <summary>
    ///     Confirms a stored traceparent value links processor activities to the inbound parent context.
    /// </summary>
    [Fact]
    public void TryGetParentActivityContext_with_traceparent_should_parse_parent()
    {
        using var parent = new Activity("parent");
        parent.Start();
        var traceContext = parent.Id;

        var parsed = MessageProcessorDiagnostics.TryGetParentActivityContext(traceContext, out var parentContext);

        parsed.Should().BeTrue();
        parentContext.TraceId.Should().Be(parent.TraceId);
        parentContext.SpanId.Should().Be(parent.SpanId);
    }

    /// <summary>
    ///     Confirms blank trace context does not supply a parent activity context.
    /// </summary>
    [Fact]
    public void TryGetParentActivityContext_without_traceparent_should_return_false()
    {
        var parsed = MessageProcessorDiagnostics.TryGetParentActivityContext(null, out _);

        parsed.Should().BeFalse();
    }
}
