using LiteBus.Transport.Amqp;

namespace LiteBus.Transport.Amqp.UnitTests;

/// <summary>
///     Verifies acknowledgement policy for AMQP consumer handler exceptions.
/// </summary>
public sealed class AmqpConsumerAckPolicyTests
{
    /// <summary>
    ///     Verifies graceful shutdown cancellation does not negative-acknowledge the delivery.
    /// </summary>
    [Fact]
    public void ShouldNack_when_shutdownCanceled_ShouldReturnFalse()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var shouldNack = AmqpConsumerAckPolicy.ShouldNack(new OperationCanceledException(), cts.Token);

        shouldNack.Should().BeFalse();
    }

    /// <summary>
    ///     Verifies handler failures still negative-acknowledge the delivery.
    /// </summary>
    [Fact]
    public void ShouldNack_when_handlerFails_ShouldReturnTrue()
    {
        var shouldNack = AmqpConsumerAckPolicy.ShouldNack(
            new InvalidOperationException("handler failed"),
            CancellationToken.None);

        shouldNack.Should().BeTrue();
    }
}
