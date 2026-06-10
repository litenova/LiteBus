using Amazon.SQS.Model;
using AwesomeAssertions;
using LiteBus.Transport.Aws;

namespace LiteBus.Transport.Aws.UnitTests;

/// <summary>
///     Verifies SQS requeue visibility and poll backoff calculations.
/// </summary>
public sealed class SqsRequeueBackoffTests
{
    /// <summary>
    ///     Verifies requeue visibility honors <c>ApproximateReceiveCount</c> for exponential backoff.
    /// </summary>
    [Fact]
    public void ComputeRequeueVisibilityTimeout_shouldHonorReceiveCount()
    {
        var message = new Message
        {
            Attributes = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["ApproximateReceiveCount"] = "3"
            }
        };

        var timeout = SqsRequeueBackoff.ComputeRequeueVisibilityTimeout(
            message,
            new AwsSqsTransportOptions
            {
                RequeueVisibilityTimeoutSeconds = 10,
                RequeueBackoffMultiplier = 2.0,
                MaxRequeueVisibilityTimeoutSeconds = 900
            });

        timeout.Should().Be(40);
    }

    /// <summary>
    ///     Verifies poll backoff increases with consecutive full-batch failures.
    /// </summary>
    [Fact]
    public void ComputePollBackoff_shouldIncreaseWithBatchFailures()
    {
        var options = new AwsSqsTransportOptions
        {
            PollBackoffInitial = TimeSpan.FromMilliseconds(500),
            PollBackoffMultiplier = 2.0,
            PollBackoffMax = TimeSpan.FromSeconds(30)
        };

        var first = SqsRequeueBackoff.ComputePollBackoff(1, options);
        var second = SqsRequeueBackoff.ComputePollBackoff(2, options);

        first.Should().Be(TimeSpan.FromMilliseconds(500));
        second.Should().Be(TimeSpan.FromSeconds(1));
    }
}
