using System.Text.Json;
using LiteBus.Inbox.Abstractions.Exceptions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Inbox.UnitTests.Ingress;

/// <summary>
///     Verifies acknowledgement policy on <see cref="IngressAckPolicy" />.
/// </summary>
public sealed class IngressAckPolicyTests
{
    /// <summary>
    ///     Verifies poison and capacity failures are discarded when requeue on failure is enabled.
    /// </summary>
    /// <param name="exception">The exception thrown during acceptance.</param>
    [Theory]
    [InlineData(typeof(MessageContractNotRegisteredException))]
    [InlineData(typeof(InboxDispatchException))]
    [InlineData(typeof(InboxIngressException))]
    [InlineData(typeof(InboxStorageException))]
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(ArgumentException))]
    [InlineData(typeof(FormatException))]
    [InlineData(typeof(JsonException))]
    public void ShouldRequeue_WhenRequeueOnFailureTrueAndDiscardException_ShouldReturnFalse(Type exception)
    {
        var instance = CreateException(exception);
        IngressAckPolicy.ShouldRequeue(instance, true).Should().BeFalse();
    }

    /// <summary>
    ///     Verifies transient failures requeue when requeue on failure is enabled.
    /// </summary>
    [Fact]
    public void ShouldRequeue_WhenRequeueOnFailureTrueAndTransientIOException_ShouldReturnTrue()
    {
        IngressAckPolicy.ShouldRequeue(new IOException("disk full"), true).Should().BeTrue();
    }

    /// <summary>
    ///     Verifies all failures discard when requeue on failure is disabled.
    /// </summary>
    [Fact]
    public void ShouldRequeue_WhenRequeueOnFailureFalse_ShouldReturnFalseForTransientFailure()
    {
        IngressAckPolicy.ShouldRequeue(new IOException("disk full"), false).Should().BeFalse();
    }

    /// <summary>
    ///     Creates an exception instance for the supplied exception type.
    /// </summary>
    /// <param name="exceptionType">The exception type to instantiate.</param>
    /// <returns>The created exception.</returns>
    private static Exception CreateException(Type exceptionType)
    {
        if (exceptionType == typeof(MessageContractNotRegisteredException))
        {
            return new MessageContractNotRegisteredException("missing.contract", 1);
        }

        if (exceptionType == typeof(InboxDispatchException))
        {
            return new InboxDispatchException("missing header");
        }

        if (exceptionType == typeof(InboxIngressException))
        {
            return new InboxIngressException("ingress rejected");
        }

        if (exceptionType == typeof(InboxStorageException))
        {
            return new InboxStorageException("capacity exceeded");
        }

        if (exceptionType == typeof(JsonException))
        {
            return new JsonException("invalid json");
        }

        return (Exception) Activator.CreateInstance(exceptionType, "test")!;
    }
}