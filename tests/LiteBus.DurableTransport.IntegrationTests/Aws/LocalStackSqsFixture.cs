using Amazon;
using Amazon.Runtime;
using Amazon.SQS;
using Amazon.SQS.Model;
using LiteBus.DurableTransport.IntegrationTesting;
using LiteBus.Transport.Aws;
using Testcontainers.LocalStack;

namespace LiteBus.DurableTransport.IntegrationTests.Aws;

/// <summary>
///     Shared LocalStack fixture that provisions SQS queues for durable transport integration tests.
/// </summary>
public sealed class LocalStackSqsFixture : IAsyncLifetime
{
    /// <summary>
    ///     Gets the transport options for the started LocalStack container.
    /// </summary>
    public AwsSqsTransportOptions TransportOptions { get; private set; } = null!;

    /// <summary>
    ///     Gets the SQS client bound to the LocalStack endpoint.
    /// </summary>
    public IAmazonSQS SqsClient { get; private set; } = null!;

    /// <summary>
    ///     The running LocalStack test container.
    /// </summary>
    private LocalStackContainer? _container;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        await DockerTestGate.RunAsync(async () =>
        {
            _container = new LocalStackBuilder()
                .WithImage("localstack/localstack:4.2")
                .Build();

            await _container.StartAsync().ConfigureAwait(false);

            var serviceUrl = _container.GetConnectionString();
            TransportOptions = new AwsSqsTransportOptions
            {
                ServiceUrl = serviceUrl,
                Region = RegionEndpoint.USEast1.SystemName,
                AccessKey = "test",
                SecretKey = "test"
            };

            SqsClient = new AmazonSQSClient(
                new BasicAWSCredentials("test", "test"),
                new AmazonSQSConfig
                {
                    ServiceURL = serviceUrl,
                    AuthenticationRegion = RegionEndpoint.USEast1.SystemName
                });
        }).ConfigureAwait(false);
    }

    /// <summary>
    ///     Creates a new SQS queue and returns its queue URL.
    /// </summary>
    /// <param name="prefix">The prefix identifying the scenario under test.</param>
    /// <returns>The queue URL used as a transport destination.</returns>
    public async Task<string> CreateQueueAsync(string prefix)
    {
        var response = await SqsClient.CreateQueueAsync(new CreateQueueRequest
        {
            QueueName = $"litebus-{prefix}-{Guid.NewGuid():N}"
        }).ConfigureAwait(false);

        return response.QueueUrl;
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        SqsClient?.Dispose();

        if (_container is not null)
        {
            await _container.DisposeAsync().ConfigureAwait(false);
        }
    }
}
