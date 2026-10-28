using System.Text.Json;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Inbox.UnitTests.ReleaseFixes;

/// <summary>
///     Verifies malformed payloads discard instead of requeueing forever.
/// </summary>
public sealed class IngressAckPolicyReleaseFixesTests
{
    /// <summary>
    ///     Verifies serialization failures discard when requeue on failure is enabled.
    /// </summary>
    [Fact]
    public void ShouldRequeue_WhenMessageSerializationException_ShouldReturnFalse()
    {
        var exception = new MessageSerializationException(
            typeof(ProbeCommand),
            "deserialized",
            new JsonException("The body is not valid JSON."));

        IngressAckPolicy.ShouldRequeue(exception, true).Should().BeFalse();
    }

    /// <summary>
    ///     Verifies serialization failures discard even when wrapped in a requeueable exception.
    /// </summary>
    [Fact]
    public void ShouldRequeue_WhenSerializationFailureIsWrapped_ShouldReturnFalse()
    {
        var exception = new IOException(
            "transport wrapper",
            new MessageSerializationException(
                typeof(ProbeCommand),
                "deserialized",
                new JsonException("The body is not valid JSON.")));

        IngressAckPolicy.ShouldRequeue(exception, true).Should().BeFalse();
    }

    /// <summary>
    ///     Verifies wrapped JSON failures discard through inner-chain inspection.
    /// </summary>
    [Fact]
    public void ShouldRequeue_WhenJsonExceptionIsWrapped_ShouldReturnFalse()
    {
        var exception = new IOException("transport wrapper", new JsonException("The body is not valid JSON."));

        IngressAckPolicy.ShouldRequeue(exception, true).Should().BeFalse();
    }

    /// <summary>
    ///     Verifies wrapped transient failures still requeue when no poison type appears in the chain.
    /// </summary>
    [Fact]
    public void ShouldRequeue_WhenTransientFailureIsWrapped_ShouldReturnTrue()
    {
        var exception = new IOException("transient", new IOException("transient inner"));

        IngressAckPolicy.ShouldRequeue(exception, true).Should().BeTrue();
    }

    /// <summary>
    ///     Verifies a malformed delivery body is discarded without requeue at the consumer boundary.
    /// </summary>
    /// <returns>A task that completes when the discard assertion succeeds.</returns>
    [Fact]
    public async Task HandleDeliveryAsync_WhenBodyIsMalformedJson_ShouldDiscardWithoutRequeue()
    {
        var ackCount = 0;
        var nackRequeue = new List<bool>();
        var consumer = CreateConsumer();

        await IngressConsumerReleaseFixesSupport.InvokeHandleDeliveryAsync(
            consumer,
            IngressConsumerReleaseFixesSupport.CreateMessage(
                "{not-valid-json"u8.ToArray(),
                () =>
                {
                    ackCount++;
                    return Task.CompletedTask;
                },
                (requeue, _) =>
                {
                    nackRequeue.Add(requeue);
                    return Task.CompletedTask;
                })).ConfigureAwait(false);

        ackCount.Should().Be(0);
        nackRequeue.Should().ContainSingle().Which.Should().BeFalse();
    }

    /// <summary>
    ///     Creates an ingress consumer wired to a real handler and an in-memory inbox.
    /// </summary>
    /// <returns>The configured ingress consumer.</returns>
    private static TransportInboxIngressConsumer CreateConsumer()
    {
        var contractRegistry = new MessageContractRegistry();
        contractRegistry.Register<ProbeCommand>("probe.command");

        var inbox = InboxWriterTestFactory.Create(
            new InMemoryInboxStore(),
            contractRegistry,
            new SystemTextJsonMessageSerializer(),
            TimeProvider.System);

        return IngressConsumerReleaseFixesSupport.CreateConsumer(
            inbox,
            new TransportInboxIngressOptions
            {
                RequeueOnFailure = true,
                Safety = new TransportInboxIngressSafetyOptions
                {
                    RequireStableIdentity = false
                }
            });
    }

    /// <summary>
    ///     Probe command type used by acknowledgement release tests.
    /// </summary>
    /// <param name="Value">The probe payload value.</param>
    private sealed record ProbeCommand(int Value);
}
