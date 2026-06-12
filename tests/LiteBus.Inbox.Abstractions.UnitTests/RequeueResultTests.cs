using LiteBus.Inbox.Abstractions;

namespace LiteBus.Inbox.Abstractions.UnitTests;

/// <summary>
///     Verifies dead-letter requeue result semantics.
/// </summary>
public sealed class RequeueResultTests
{
    /// <summary>
    ///     Verifies the record exposes requested and requeued counts independently.
    /// </summary>
    [Fact]
    public void RequeueResult_should_expose_requested_and_requeued_counts()
    {
        var result = new RequeueResult(Requested: 3, Requeued: 2);

        result.Requested.Should().Be(3);
        result.Requeued.Should().Be(2);
    }
}
