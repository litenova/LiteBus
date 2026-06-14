using LiteBus.Transport.IntegrationTesting;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Dispatch.AwsSqs;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions.DurableMessaging;
using LiteBus.Testing;
using LiteBus.Transport.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Inbox.Dispatch.AwsSqs.IntegrationTests;

/// <summary>
///     End-to-end inbox dispatch integration tests for the AWS SQS transport adapter.
/// </summary>
[Collection(LocalStackSqsCollection.Name)]
[Trait("Category", TransportTestTraits.Docker)]
public sealed class AwsSqsInboxDispatchIntegrationTests : LiteBusTestBase
{
    private const string ContractName = "tests.remote-work";
    private const int ContractVersion = 1;

    /// <summary>
    ///     The shared LocalStack SQS fixture.
    /// </summary>
    private readonly LocalStackSqsFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AwsSqsInboxDispatchIntegrationTests" /> class.
    /// </summary>
    /// <param name="fixture">The shared LocalStack SQS fixture.</param>
    public AwsSqsInboxDispatchIntegrationTests(LocalStackSqsFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    ///     Verifies that processing a leased inbox envelope publishes payload and headers to SQS.
    /// </summary>
    /// <returns>A task that completes when the publish assertion succeeds.</returns>
    [Fact]
    public async Task ProcessPendingAsync_ShouldPublishLeasedEnvelopeToSqsQueue()
    {
        var queueUrl = await _fixture.CreateQueueAsync("inbox-dispatch").ConfigureAwait(false);
         var provider = BuildProvider(queueUrl);
         await using (provider.ConfigureAwait(false))
         {

        var inbox = provider.GetRequiredService<IInbox>();
        var processor = provider.GetRequiredService<IInboxProcessor>();

        var workItemId = Guid.NewGuid();

        var receipt = await inbox.AcceptAsync(new InboxAcceptItem<RemoteWorkCommand>
        {
            Message = new RemoteWorkCommand
            {
                WorkItemId = workItemId,
                IdempotencyKey = $"work:{workItemId}"
            },
            Metadata = InboxAcceptMetadata.Immediate with
            {
                Trace = new MessageTrace.Correlated("corr-sqs-dispatch")
            }
        }).ConfigureAwait(false);

        await processor.ProcessPendingAsync().ConfigureAwait(false);

        var (body, headers) = await SqsTransportTestInfrastructure.ReceiveOneAsync(
            _fixture.SqsClient,
            queueUrl,
            TimeSpan.FromSeconds(30)).ConfigureAwait(false);

        body.Should().Contain(workItemId.ToString());
        headers[TransportHeaders.MessageId].Should().Be(receipt.Id.ToString("D"));
        headers[TransportHeaders.ContractName].Should().Be(ContractName);
        headers[TransportHeaders.CorrelationId].Should().Be("corr-sqs-dispatch");
        }
    }

    /// <summary>
    ///     Builds a LiteBus service provider configured for SQS inbox dispatch tests.
    /// </summary>
    /// <param name="queueUrl">The SQS queue URL used for dispatch.</param>
    /// <returns>The configured service provider.</returns>
    private ServiceProvider BuildProvider(string queueUrl)
    {
        return new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessageModule(_ =>
                {
                });

                registry.AddInboxModule(inbox =>
                {
                    inbox.Contracts.Register<RemoteWorkCommand>(ContractName);

                    inbox.UseProcessorOptions(new InboxProcessorOptions
                    {
                        BatchSize = 10,
                        LeaseOwner = "sqs-dispatch-test",
                        Retry = new RetryOptions { UseJitter = false }
                    });

                    inbox.UseInMemoryStorage();

                    inbox.UseAwsSqsDispatch(
                        transport => transport.DefaultDestination = queueUrl,
                        _fixture.TransportOptions);
                });
            })
            .BuildServiceProvider();
    }
}
