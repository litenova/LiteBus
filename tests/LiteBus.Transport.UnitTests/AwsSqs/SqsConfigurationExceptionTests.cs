using Amazon.Runtime;
using Amazon.SQS;
using LiteBus.Transport.AwsSqs;
using LiteBus.Transport.AwsSqs.Exceptions;

namespace LiteBus.Transport.UnitTests.AwsSqs;

/// <summary>
///     Verifies the SQS configuration exception shape and the client-side failure classification.
/// </summary>
public sealed class SqsConfigurationExceptionTests
{
    /// <summary>
    ///     Verifies a client-side SDK failure is classified as a configuration failure.
    /// </summary>
    [Fact]
    public void IsConfigurationFailure_ClientException_ReturnsTrue()
    {
        SqsPublisher.IsConfigurationFailure(
            new AmazonClientException("No RegionEndpoint or ServiceURL configured.")).Should().BeTrue();
    }

    /// <summary>
    ///     Verifies service responses are not classified as configuration failures.
    /// </summary>
    [Fact]
    public void IsConfigurationFailure_ServiceException_ReturnsFalse()
    {
        SqsPublisher.IsConfigurationFailure(new AmazonSQSException("Service unavailable.")).Should().BeFalse();
    }

    /// <summary>
    ///     Verifies unrelated failures are not classified as configuration failures.
    /// </summary>
    [Fact]
    public void IsConfigurationFailure_UnrelatedException_ReturnsFalse()
    {
        SqsPublisher.IsConfigurationFailure(new InvalidOperationException("Unexpected.")).Should().BeFalse();
    }

    /// <summary>
    ///     Verifies the exception preserves its message.
    /// </summary>
    [Fact]
    public void Constructor_WithMessage_PreservesMessage()
    {
        new AwsSqsTransportConfigurationException("SQS is not configured.").Message
            .Should().Be("SQS is not configured.");
    }

    /// <summary>
    ///     Verifies the exception preserves its inner exception.
    /// </summary>
    [Fact]
    public void Constructor_WithInnerException_PreservesInnerException()
    {
        var inner = new AmazonClientException("No RegionEndpoint or ServiceURL configured.");

        var exception = new AwsSqsTransportConfigurationException("SQS is not configured.", inner);

        exception.InnerException.Should().Be(inner);
    }
}
