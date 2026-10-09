using LiteBus.Transport.Abstractions;
using LiteBus.Transport.AwsSqs;
using LiteBus.Transport.IntegrationTesting.Aws;
using LiteBus.Transport.Testing;

namespace LiteBus.Transport.IntegrationTests.Aws;

/// <summary>
///     Runs the shared transport conformance suite against Amazon SQS.
/// </summary>
[Collection(LocalStackSqsCollection.Name)]
[Trait("Category", TransportTestTraits.Docker)]
public sealed class SqsTransportContractTests : TransportContractTests
{
    /// <summary>
    ///     The shared LocalStack SQS fixture.
    /// </summary>
    private readonly LocalStackSqsFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SqsTransportContractTests" /> class.
    /// </summary>
    /// <param name="fixture">The shared LocalStack SQS fixture.</param>
    public SqsTransportContractTests(LocalStackSqsFixture fixture)
    {
        _fixture = fixture;
    }

    /// <inheritdoc />
    protected override async ValueTask<TransportContractContext> CreateContextAsync(string scenario)
    {
        var queueUrl = await _fixture.CreateQueueAsync($"contract-{scenario}").ConfigureAwait(false);
        var publisher = new SqsPublisher(_fixture.SqsClient, new TransportCircuitBreakerRegistry());
        var consumer = new SqsConsumer(_fixture.SqsClient, _fixture.TransportOptions);

        return new TransportContractContext(
            publisher,
            consumer,
            new TransportConsumerOptions { Destination = queueUrl },
            queueUrl,
            async () =>
            {
                await consumer.DisposeAsync().ConfigureAwait(false);
            });
    }
}
