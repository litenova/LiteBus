using LiteBus.Messaging;
using LiteBus.Outbox.Abstractions;

namespace LiteBus.Outbox.UnitTests.ReleaseFixes;

/// <summary>
///     Verifies the outbox envelope factory rejects non-JSON distributed trace context before persistence.
/// </summary>
public sealed class R3F4TraceJsonValidationTests
{
    /// <summary>
    ///     Verifies a raw traceparent string fails fast instead of persisting a poison row.
    /// </summary>
    [Fact]
    public async Task CreateAsync_with_non_json_trace_context_should_throw_argument_exception()
    {
        var factory = CreateFactory();
        var metadata = OutboxEnqueueMetadata.Immediate with
        {
            Trace = new MessageTrace.Distributed("corr-1", "cause-1", "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01")
        };
        var item = OutboxEnqueueItem<TestEvent>.From(new TestEvent(), metadata);

        var act = () => factory.CreateAsync(item);

        var failure = await act.Should().ThrowAsync<ArgumentException>().ConfigureAwait(false);
        failure.Which.Message.Should().Contain("JSON");
    }

    /// <summary>
    ///     Verifies valid JSON trace context still creates an envelope.
    /// </summary>
    [Fact]
    public async Task CreateAsync_with_json_trace_context_should_succeed()
    {
        var factory = CreateFactory();
        var metadata = OutboxEnqueueMetadata.Immediate with
        {
            Trace = new MessageTrace.Distributed("corr-1", "cause-1", """{"traceparent":"00-abc-def-01"}""")
        };
        var item = OutboxEnqueueItem<TestEvent>.From(new TestEvent(), metadata);

        var envelope = await factory.CreateAsync(item).ConfigureAwait(false);

        envelope.TraceContext.Should().Be("""{"traceparent":"00-abc-def-01"}""");
    }

    private static OutboxEnvelopeFactory CreateFactory()
    {
        var registry = new MessageContractRegistry();
        registry.Register<TestEvent>("tests.events.sample", 1);
        return new OutboxEnvelopeFactory(registry, new SystemTextJsonMessageSerializer(), TimeProvider.System);
    }

    private sealed record TestEvent;
}
