using System.Text;
using Amazon.SQS.Model;
using LiteBus.Transport.Abstractions;
using LiteBus.Transport.AwsSqs;
using LiteBus.Transport.IntegrationTesting.Aws;

namespace LiteBus.Transport.IntegrationTests.Aws;

/// <summary>
///     Verifies SQS FIFO deduplication and ordering through the LiteBus publisher.
/// </summary>
[Collection(LocalStackSqsCollection.Name)]
[Trait("Category", TransportTestTraits.Docker)]
public sealed class SqsFifoPublishIntegrationTests
{
    /// <summary>
    ///     The shared LocalStack SQS fixture.
    /// </summary>
    private readonly LocalStackSqsFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SqsFifoPublishIntegrationTests" /> class.
    /// </summary>
    /// <param name="fixture">The shared LocalStack SQS fixture.</param>
    public SqsFifoPublishIntegrationTests(LocalStackSqsFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    ///     Verifies that a repeated deduplication key is delivered once and publish order is preserved.
    /// </summary>
    /// <returns>A task that completes when the FIFO delivery is verified.</returns>
    [Fact]
    public async Task PublishAsync_WithDuplicateDeduplicationKey_DeliversTwoMessagesInOrder()
    {
        var createResponse = await _fixture.SqsClient.CreateQueueAsync(new CreateQueueRequest
        {
            QueueName = $"litebus-fifo-{Guid.NewGuid():N}.fifo",
            Attributes = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["FifoQueue"] = "true",
                ["ContentBasedDeduplication"] = "false"
            }
        }).ConfigureAwait(false);
        var queueUrl = createResponse.QueueUrl;

        ITransportPublisher publisher = new SqsPublisher(_fixture.SqsClient, new TransportCircuitBreakerRegistry());

        const string messageGroupId = "fifo-group-1";
        const string firstDeduplicationKey = "dedup-1";
        const string secondDeduplicationKey = "dedup-2";
        const string firstBody = "fifo-first";
        const string duplicateBody = "fifo-duplicate";
        const string thirdBody = "fifo-third";

        await publisher.PublishAsync(new TransportPublishRequest
        {
            Destination = queueUrl,
            Body = Encoding.UTF8.GetBytes(firstBody),
            MessageGroupId = messageGroupId,
            DeduplicationKey = firstDeduplicationKey
        }).ConfigureAwait(false);

        await publisher.PublishAsync(new TransportPublishRequest
        {
            Destination = queueUrl,
            Body = Encoding.UTF8.GetBytes(duplicateBody),
            MessageGroupId = messageGroupId,
            DeduplicationKey = firstDeduplicationKey
        }).ConfigureAwait(false);

        await publisher.PublishAsync(new TransportPublishRequest
        {
            Destination = queueUrl,
            Body = Encoding.UTF8.GetBytes(thirdBody),
            MessageGroupId = messageGroupId,
            DeduplicationKey = secondDeduplicationKey
        }).ConfigureAwait(false);

        await SqsTransportTestInfrastructure.WaitForQueueDepthAsync(
            _fixture.SqsClient,
            queueUrl,
            2,
            TimeSpan.FromSeconds(30)).ConfigureAwait(false);

        var response = await _fixture.SqsClient.ReceiveMessageAsync(new ReceiveMessageRequest
        {
            QueueUrl = queueUrl,
            MaxNumberOfMessages = 10,
            WaitTimeSeconds = 5,
            MessageAttributeNames = ["All"],
            MessageSystemAttributeNames = ["All"]
        }).ConfigureAwait(false);

        response.Messages.Count.Should().Be(2);
        response.Messages[0].Body.Should().Be(firstBody);
        response.Messages[1].Body.Should().Be(thirdBody);

        foreach (var message in response.Messages)
        {
            message.Attributes.Should().ContainKey("MessageGroupId");
            message.Attributes["MessageGroupId"].Should().Be(messageGroupId);
        }
    }
}
