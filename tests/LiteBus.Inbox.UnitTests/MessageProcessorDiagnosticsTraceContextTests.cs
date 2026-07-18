using System.Diagnostics;
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
    ///     Confirms a serialized trace context object supplies the stored remote parent and trace state.
    /// </summary>
    [Fact]
    public void TryGetParentActivityContext_with_json_context_should_parse_parent()
    {
        const string traceParent = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01";
        const string traceContext = $$"""{"traceparent":"{{traceParent}}","tracestate":"vendor=value"}""";

        var parsed = MessageProcessorDiagnostics.TryGetParentActivityContext(traceContext, out var parentContext);

        parsed.Should().BeTrue();
        parentContext.TraceId.ToHexString().Should().Be("4bf92f3577b34da6a3ce929d0e0e4736");
        parentContext.SpanId.ToHexString().Should().Be("00f067aa0ba902b7");
        parentContext.TraceState.Should().Be("vendor=value");
        parentContext.IsRemote.Should().BeTrue();
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
