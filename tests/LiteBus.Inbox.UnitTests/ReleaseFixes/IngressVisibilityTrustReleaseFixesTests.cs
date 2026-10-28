using LiteBus.Transport.Abstractions;

namespace LiteBus.Inbox.UnitTests.ReleaseFixes;

/// <summary>
///     Verifies distrusted visibility headers are ignored while correlation headers are still honored.
/// </summary>
public sealed class IngressVisibilityTrustReleaseFixesTests
{
    /// <summary>
    ///     Verifies absolute visibility headers are ignored when application headers are distrusted.
    /// </summary>
    [Fact]
    public void ToInboxAcceptMetadata_WhenDistrusted_ShouldIgnoreVisibleAfterHeader()
    {
        var visibleAfter = new DateTimeOffset(2036, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var metadata = TransportInboxIngressMapper.ToInboxAcceptMetadata(
            CreateMessage(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [TransportHeaders.VisibleAfter] = visibleAfter.ToString("O")
            }),
            new TransportInboxIngressMappingOptions(RequireStableIdentity: true, TrustApplicationHeaders: false));

        metadata.Visibility.Should().Be(MessageVisibility.Immediate.Instance);
    }

    /// <summary>
    ///     Verifies relative visibility delay headers are ignored when application headers are distrusted.
    /// </summary>
    [Fact]
    public void ToInboxAcceptMetadata_WhenDistrusted_ShouldIgnoreVisibleAfterDelayHeader()
    {
        var metadata = TransportInboxIngressMapper.ToInboxAcceptMetadata(
            CreateMessage(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [TransportHeaders.VisibleAfterDelay] = TimeSpan.FromDays(30).ToString("c")
            }),
            new TransportInboxIngressMappingOptions(RequireStableIdentity: true, TrustApplicationHeaders: false));

        metadata.Visibility.Should().Be(MessageVisibility.Immediate.Instance);
    }

    /// <summary>
    ///     Verifies trusted visibility headers still schedule deferred execution.
    /// </summary>
    [Fact]
    public void ToInboxAcceptMetadata_WhenTrusted_ShouldHonorVisibleAfterHeader()
    {
        var visibleAfter = new DateTimeOffset(2026, 6, 5, 12, 0, 0, TimeSpan.Zero);

        var metadata = TransportInboxIngressMapper.ToInboxAcceptMetadata(
            CreateMessage(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [TransportHeaders.VisibleAfter] = visibleAfter.ToString("O")
            }),
            new TransportInboxIngressMappingOptions(RequireStableIdentity: true, TrustApplicationHeaders: true));

        metadata.Visibility.Should().Be(new MessageVisibility.At(visibleAfter));
    }

    /// <summary>
    ///     Verifies correlation headers are honored even when application headers are distrusted.
    /// </summary>
    [Fact]
    public void ToInboxAcceptMetadata_WhenDistrusted_ShouldStillHonorCorrelationHeaders()
    {
        var metadata = TransportInboxIngressMapper.ToInboxAcceptMetadata(
            CreateMessage(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [TransportHeaders.CorrelationId] = "corr-1",
                [TransportHeaders.CausationId] = "cause-1"
            }),
            new TransportInboxIngressMappingOptions(RequireStableIdentity: true, TrustApplicationHeaders: false));

        metadata.Trace.Should().Be(new MessageTrace.Workflow("corr-1", "cause-1"));
    }

    /// <summary>
    ///     Creates a transport message with the supplied headers and a stable broker delivery id.
    /// </summary>
    /// <param name="headers">The application headers.</param>
    /// <returns>A transport message suitable for mapper assertions.</returns>
    private static TransportMessage CreateMessage(IReadOnlyDictionary<string, object?> headers)
    {
        return new TransportMessage
        {
            Body = ReadOnlyMemory<byte>.Empty,
            Headers = headers,
            MessageId = "broker-delivery-7",
            Destination = "commands.inbox",
            AckAsync = _ => Task.CompletedTask,
            NackAsync = (_, _) => Task.CompletedTask
        };
    }
}
