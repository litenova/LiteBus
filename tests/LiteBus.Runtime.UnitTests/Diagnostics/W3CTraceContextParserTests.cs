using System.Diagnostics;
using LiteBus.Runtime.Abstractions.Diagnostics;

namespace LiteBus.Runtime.UnitTests.Diagnostics;

/// <summary>
///     Verifies supported W3C trace context storage formats and malformed input handling.
/// </summary>
public sealed class W3CTraceContextParserTests
{
    /// <summary>
    ///     Verifies a direct trace parent string produces a remote activity context.
    /// </summary>
    [Fact]
    public void TryParse_WithTraceParentString_ShouldParseRemoteContext()
    {
        const string traceParent = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01";

        var parsed = W3CTraceContextParser.TryParse(traceParent, out var context);

        parsed.Should().BeTrue();
        context.TraceId.ToHexString().Should().Be("4bf92f3577b34da6a3ce929d0e0e4736");
        context.SpanId.ToHexString().Should().Be("00f067aa0ba902b7");
        context.IsRemote.Should().BeTrue();
    }

    /// <summary>
    ///     Verifies malformed or structurally invalid stored contexts are rejected without throwing.
    /// </summary>
    /// <param name="serializedContext">The invalid stored context.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-trace-parent")]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("{\"traceparent\":42}")]
    [InlineData("{\"traceparent\":\"invalid\"}")]
    [InlineData("{\"traceparent\":\"00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01\",\"tracestate\":42}")]
    public void TryParse_WithInvalidContext_ShouldReturnFalse(string? serializedContext)
    {
        var parsed = W3CTraceContextParser.TryParse(serializedContext, out var context);

        parsed.Should().BeFalse();
        context.Should().Be(default(ActivityContext));
    }
}
