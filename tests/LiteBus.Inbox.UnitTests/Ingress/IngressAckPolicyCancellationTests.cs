using System.Reflection;

namespace LiteBus.Inbox.UnitTests.Ingress;

/// <summary>
///     Verifies cancelled ingress acceptance never requeues.
/// </summary>
public sealed class IngressAckPolicyCancellationTests
{
    /// <summary>
    ///     Verifies a cancelled acceptance leaves the delivery unacknowledged instead of requeueing it.
    /// </summary>
    [Fact]
    public void ShouldRequeue_WhenAcceptanceWasCancelled_ShouldReturnFalse()
    {
        IngressAckPolicy.ShouldRequeue(new OperationCanceledException("The accept was cancelled."), true).Should().BeFalse();
    }

    /// <summary>
    ///     Verifies a task-cancelled acceptance leaves the delivery unacknowledged instead of requeueing it.
    /// </summary>
    [Fact]
    public void ShouldRequeue_WhenAcceptanceWasTaskCancelled_ShouldReturnFalse()
    {
        IngressAckPolicy.ShouldRequeue(new TaskCanceledException("The accept was cancelled."), true).Should().BeFalse();
    }

    /// <summary>
    ///     Verifies a wrapped cancellation unwinds to the same no-requeue outcome.
    /// </summary>
    [Fact]
    public void ShouldRequeue_WhenCancellationWasWrapped_ShouldReturnFalse()
    {
        var wrapped = new TargetInvocationException(new OperationCanceledException("The accept was cancelled."));

        IngressAckPolicy.ShouldRequeue(wrapped, true).Should().BeFalse();
    }
}
