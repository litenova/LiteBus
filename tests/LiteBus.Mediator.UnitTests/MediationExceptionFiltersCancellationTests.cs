using LiteBus.Messaging.Abstractions;

namespace LiteBus.Mediator.UnitTests;

/// <summary>
///     Verifies caller-cancellation classification and retryability of wrapped dispatch failures.
/// </summary>
public sealed class MediationExceptionFiltersCancellationTests
{
    /// <summary>
    ///     Verifies an uncancelled token never reports caller cancellation.
    /// </summary>
    [Fact]
    public void IsCallerCancellation_when_token_not_cancelled_should_return_false()
    {
        MediationExceptionFilters.IsCallerCancellation(new OperationCanceledException(), CancellationToken.None)
            .Should().BeFalse();
    }

    /// <summary>
    ///     Verifies a cancelled token with a cancellation reports caller cancellation.
    /// </summary>
    [Fact]
    public void IsCallerCancellation_when_token_cancelled_with_cancellation_should_return_true()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        MediationExceptionFilters.IsCallerCancellation(new OperationCanceledException(), cancellation.Token)
            .Should().BeTrue();
    }

    /// <summary>
    ///     Verifies a cancellation hidden inside a wrapper still reports caller cancellation.
    /// </summary>
    [Fact]
    public void IsCallerCancellation_when_wrapper_hides_cancellation_should_return_true()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var wrapped = new InvalidOperationException("dispatch failed", new OperationCanceledException());

        MediationExceptionFilters.IsCallerCancellation(wrapped, cancellation.Token).Should().BeTrue();
    }

    /// <summary>
    ///     Verifies a genuine failure is not mistaken for caller cancellation.
    /// </summary>
    [Fact]
    public void IsCallerCancellation_when_failure_without_cancellation_should_return_false()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        MediationExceptionFilters.IsCallerCancellation(
                new InvalidOperationException("handler failed"),
                cancellation.Token)
            .Should().BeFalse();
    }

    /// <summary>
    ///     Verifies a missing-handler failure hidden inside a wrapper is not retryable.
    /// </summary>
    [Fact]
    public void IsRetryableDispatchException_when_wrapper_hides_missing_handler_should_return_false()
    {
        var wrapped = new InvalidOperationException(
            "dispatch failed",
            new NoHandlerFoundException(typeof(object)));

        MediationExceptionFilters.IsRetryableDispatchException(wrapped).Should().BeFalse();
    }
}
