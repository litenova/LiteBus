using LiteBus.Testing;
using LiteBus.Transport.Abstractions;

namespace LiteBus.Outbox.UnitTests.Testing;

/// <summary>
///     Verifies the one-shot failure and concurrent publish behavior of <see cref="TestMessageTransport" />.
/// </summary>
[Collection("Sequential")]
public sealed class TestMessageTransportTests : LiteBusTestBase
{
    /// <summary>
    ///     Verifies that a scripted exception fails one publish and the next publish succeeds.
    /// </summary>
    [Fact]
    public async Task PublishAsync_should_throw_the_scripted_exception_once_then_succeed()
    {
        var transport = new TestMessageTransport
        {
            NextPublishException = new TimeoutException("broker unreachable")
        };

        var request = PublishRequest();

        Func<Task> first = () => transport.PublishAsync(request);

        await first.Should().ThrowAsync<TimeoutException>().ConfigureAwait(false);
        transport.NextPublishException.Should().BeNull();
        transport.Published.Should().BeEmpty();

        await transport.PublishAsync(request).ConfigureAwait(false);

        transport.Published.Should().ContainSingle();
    }

    /// <summary>
    ///     Verifies that a scripted exception reaches exactly one of two concurrent publishes.
    /// </summary>
    [Fact]
    public async Task PublishAsync_should_deliver_a_scripted_exception_to_exactly_one_concurrent_publish()
    {
        const int trials = 100;

        for (var trial = 0; trial < trials; trial++)
        {
            var transport = new TestMessageTransport
            {
                NextPublishException = new InvalidOperationException("scripted failure")
            };

            using var gate = new ManualResetEventSlim(false);

            var first = Task.Run(() => TryPublishAsync(transport, gate));
            var second = Task.Run(() => TryPublishAsync(transport, gate));

            gate.Set();

            var outcomes = await Task.WhenAll(first, second).ConfigureAwait(false);

            outcomes.Count(threw => threw).Should().Be(1);
            transport.Published.Should().ContainSingle();
        }
    }

    /// <summary>
    ///     Publishes once the start gate opens and reports whether the scripted exception was observed.
    /// </summary>
    /// <param name="transport">The transport double under test.</param>
    /// <param name="gate">The start gate that aligns concurrent publishes.</param>
    /// <returns><see langword="true" /> when the publish observed the scripted exception; otherwise <see langword="false" />.</returns>
    private static async Task<bool> TryPublishAsync(TestMessageTransport transport, ManualResetEventSlim gate)
    {
        gate.Wait();

        try
        {
            await transport.PublishAsync(PublishRequest()).ConfigureAwait(false);
            return false;
        }
        catch (InvalidOperationException exception) when (exception.Message == "scripted failure")
        {
            return true;
        }
    }

    /// <summary>
    ///     Builds a minimal publication request for the transport double.
    /// </summary>
    /// <returns>The publication request.</returns>
    private static TransportPublishRequest PublishRequest()
    {
        return new TransportPublishRequest
        {
            Destination = "orders.outbox",
            Body = new ReadOnlyMemory<byte>([1, 2, 3])
        };
    }
}
