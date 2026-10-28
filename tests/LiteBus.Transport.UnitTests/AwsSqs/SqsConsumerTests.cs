using System.Reflection;
using Amazon.SQS;
using Amazon.SQS.Model;
using LiteBus.Transport.Abstractions;
using LiteBus.Transport.AwsSqs;

namespace LiteBus.Transport.UnitTests.AwsSqs;

/// <summary>
///     Verifies SQS consumer poison handling and stop semantics.
/// </summary>
public sealed class SqsConsumerTests
{
    /// <summary>
    ///     Verifies a message with an undecodable body is deleted as poison while the loop continues.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task PoisonBase64Body_ShouldDeletePoisonAndContinueLoop()
    {
        var client = DispatchProxy.Create<IAmazonSQS, FakeSqsProxy>();
        var fake = (FakeSqsProxy) (object) client;

        var poison = new Message
        {
            MessageId = "poison-1",
            Body = "!!!not-base64!!!",
            ReceiptHandle = "receipt-poison",
            MessageAttributes = new Dictionary<string, MessageAttributeValue>(StringComparer.Ordinal)
            {
                [TransportHeaders.ContentEncoding] = new MessageAttributeValue
                {
                    DataType = "String",
                    StringValue = "base64"
                }
            }
        };

        await RunPoisonScenarioAsync(fake, client, poison).ConfigureAwait(false);

        fake.DeletedReceipts.Should().Contain("receipt-poison");
    }

    /// <summary>
    ///     Verifies a message with malformed packed headers is deleted as poison while the loop continues.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task PoisonPackedHeaders_ShouldDeletePoisonAndContinueLoop()
    {
        var client = DispatchProxy.Create<IAmazonSQS, FakeSqsProxy>();
        var fake = (FakeSqsProxy) (object) client;

        var poison = new Message
        {
            MessageId = "poison-2",
            Body = """{"ok":true}""",
            ReceiptHandle = "receipt-poison-headers",
            MessageAttributes = new Dictionary<string, MessageAttributeValue>(StringComparer.Ordinal)
            {
                ["litebus-headers"] = new MessageAttributeValue
                {
                    DataType = "String",
                    StringValue = "{not-json"
                }
            }
        };

        await RunPoisonScenarioAsync(fake, client, poison).ConfigureAwait(false);

        fake.DeletedReceipts.Should().Contain("receipt-poison-headers");
    }

    /// <summary>
    ///     Verifies an unexpected loop failure surfaces through the wait handle instead of the stop call.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task StopAsync_WhenLoopFaulted_ShouldRouteFaultToWaitUntilStoppedAsync()
    {
        var client = DispatchProxy.Create<IAmazonSQS, FakeSqsProxy>();
        var fake = (FakeSqsProxy) (object) client;
        fake.ReceiveFunc = (_, _) => throw new InvalidOperationException("Receive boom.");

        var consumer = new SqsConsumer(client, new AwsSqsTransportOptions());
        await using (consumer.ConfigureAwait(false))
        {
            static Task OnMessage(TransportMessage message, CancellationToken token) => Task.CompletedTask;

            await consumer.StartAsync(
                new TransportConsumerOptions
                {
                    Destination = "https://sqs.us-east-1.amazonaws.com/123/orders",
                    ReceiveBatchSize = 1
                },
                OnMessage).ConfigureAwait(false);

            var waitBeforeStop = () => consumer.WaitUntilStoppedAsync();
            await waitBeforeStop.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);

            var stop = () => consumer.StopAsync();
            await stop.Should().NotThrowAsync().ConfigureAwait(false);

            var waitAfterStop = () => consumer.WaitUntilStoppedAsync();
            await waitAfterStop.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);
        }
    }

    private static async Task RunPoisonScenarioAsync(
        FakeSqsProxy fake,
        IAmazonSQS client,
        Message poison)
    {
        var queueUrl = "https://sqs.us-east-1.amazonaws.com/123/orders";
        var good = new Message
        {
            MessageId = "good-1",
            Body = """{"ok":true}""",
            ReceiptHandle = "receipt-good"
        };

        var receiveCalls = 0;
        fake.ReceiveFunc = (request, cancellationToken) =>
        {
            if (Interlocked.Increment(ref receiveCalls) == 1)
            {
                return Task.FromResult(new ReceiveMessageResponse
                {
                    Messages = [poison, good]
                });
            }

            return BlockUntilCancelledAsync(cancellationToken);
        };

        var handled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var consumer = new SqsConsumer(client, new AwsSqsTransportOptions());
        await using (consumer.ConfigureAwait(false))
        {
            await consumer.StartAsync(
                new TransportConsumerOptions { Destination = queueUrl, ReceiveBatchSize = 10 },
                async (message, cancellationToken) =>
                {
                    await message.AcceptAsync(cancellationToken).ConfigureAwait(false);
                    handled.TrySetResult();
                }).ConfigureAwait(false);

            await handled.Task.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
            await consumer.StopAsync().ConfigureAwait(false);

            fake.DeletedReceipts.Should().Contain("receipt-good");
        }
    }

    private static async Task<ReceiveMessageResponse> BlockUntilCancelledAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        return new ReceiveMessageResponse { Messages = [] };
    }

    /// <summary>
    ///     An SQS client stand-in with scriptable receive and tracked settlement calls.
    /// </summary>
    // DispatchProxy.Create needs an unsealed proxy type.
    public class FakeSqsProxy : DispatchProxy
    {
        /// <summary>
        ///     Gets or sets the receive behavior invoked for each poll.
        /// </summary>
        public Func<ReceiveMessageRequest, CancellationToken, Task<ReceiveMessageResponse>>? ReceiveFunc { get; set; }

        /// <summary>
        ///     Gets the receipt handles deleted through delete calls, in call order.
        /// </summary>
        public List<string> DeletedReceipts { get; } = [];

        /// <summary>
        ///     Gets the receipt handles with a requested visibility change, in call order.
        /// </summary>
        public List<string> VisibilityChangedReceipts { get; } = [];

        /// <summary>
        ///     Intercepts SQS calls made during receive, settlement, and visibility extension.
        /// </summary>
        /// <param name="targetMethod">The intercepted client method.</param>
        /// <param name="args">The arguments passed to the client method.</param>
        /// <returns>The stand-in result for the intercepted call.</returns>
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            ArgumentNullException.ThrowIfNull(targetMethod);

            switch (targetMethod.Name)
            {
                case "ReceiveMessageAsync" when args![0] is ReceiveMessageRequest request:
                    var cancellationToken = args.Length > 1 && args[1] is CancellationToken token
                        ? token
                        : CancellationToken.None;
                    return ReceiveFunc?.Invoke(request, cancellationToken)
                        ?? Task.FromResult(new ReceiveMessageResponse { Messages = [] });
                case "DeleteMessageAsync" when args![0] is DeleteMessageRequest delete:
                    DeletedReceipts.Add(delete.ReceiptHandle!);
                    return Task.FromResult(new DeleteMessageResponse());
                case "ChangeMessageVisibilityAsync" when args![0] is ChangeMessageVisibilityRequest change:
                    VisibilityChangedReceipts.Add(change.ReceiptHandle!);
                    return Task.FromResult(new ChangeMessageVisibilityResponse());
                default:
                    return DefaultFor(targetMethod.ReturnType);
            }
        }

        private static object? DefaultFor(Type returnType)
        {
            if (returnType == typeof(void))
            {
                return null;
            }

            if (returnType == typeof(Task))
            {
                return Task.CompletedTask;
            }

            if (returnType == typeof(ValueTask))
            {
                return default(ValueTask);
            }

            if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
            {
                var resultType = returnType.GetGenericArguments()[0];
                var fromResult = typeof(Task).GetMethod(nameof(Task.FromResult))!;
                var generic = fromResult.MakeGenericMethod(resultType);
                return generic.Invoke(null, [resultType.IsValueType ? Activator.CreateInstance(resultType) : null]);
            }

            if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(ValueTask<>))
            {
                return Activator.CreateInstance(returnType);
            }

            return returnType.IsValueType ? Activator.CreateInstance(returnType) : null;
        }
    }
}
