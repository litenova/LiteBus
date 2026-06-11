using Amazon.SQS;
using Amazon.SQS.Model;
using LiteBus.Transport.Abstractions;

namespace LiteBus.Transport.Aws;

/// <summary>
///     Consumes Amazon SQS deliveries using long polling with manual delete semantics.
/// </summary>
public sealed class SqsConsumer : IMessageConsumer
{
    /// <summary>
    ///     Serializes start and stop operations on the consume loop.
    /// </summary>
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);

    /// <summary>
    ///     Gets the transport options controlling poll and visibility behavior.
    /// </summary>
    private readonly AwsSqsTransportOptions _options;

    /// <summary>
    ///     Gets the SQS client used to receive and acknowledge messages.
    /// </summary>
    private readonly IAmazonSQS _sqsClient;

    /// <summary>
    ///     Gets the number of consecutive receive batches where every handler failed.
    /// </summary>
    private int _consecutiveFullBatchFailures;

    /// <summary>
    ///     Gets the cancellation source used to stop the active consume loop.
    /// </summary>
    private CancellationTokenSource? _consumeCts;

    /// <summary>
    ///     Gets the background task running the consume loop.
    /// </summary>
    private Task? _consumeTask;

    /// <summary>
    ///     Signals when the active consume loop stops because of shutdown or cancellation.
    /// </summary>
    private TaskCompletionSource _stoppedTcs = CreateStoppedTaskSource();

    /// <summary>
    ///     Initializes a new instance of the <see cref="SqsConsumer" /> class.
    /// </summary>
    /// <param name="sqsClient">The SQS client used to receive and acknowledge messages.</param>
    /// <param name="options">The transport options controlling poll and visibility behavior.</param>
    public SqsConsumer(IAmazonSQS sqsClient, AwsSqsTransportOptions options)
    {
        _sqsClient = sqsClient ?? throw new ArgumentNullException(nameof(sqsClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public async Task StartAsync(
        TransportConsumerOptions options,
        Func<TransportMessage, CancellationToken, Task> handler,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(handler);

        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_consumeTask is not null)
            {
                throw new InvalidOperationException("The SQS consumer is already started.");
            }

            _stoppedTcs = CreateStoppedTaskSource();
            _consumeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _consumeTask = RunConsumeLoopAsync(options, handler, _consumeCts.Token);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_consumeCts is null)
            {
                SignalStopped();
                return;
            }

            await _consumeCts.CancelAsync().ConfigureAwait(false);

            if (_consumeTask is not null)
            {
                try
                {
                    await _consumeTask.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Expected when cancellation stops the consume loop.
                }
            }

            _consumeCts.Dispose();
            _consumeCts = null;
            _consumeTask = null;
            SignalStopped();
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    /// <inheritdoc />
    public Task WaitUntilStoppedAsync(CancellationToken cancellationToken = default)
    {
        return _stoppedTcs.Task.WaitAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _lifecycleGate.Dispose();
    }

    /// <summary>
    ///     Long-polls the configured queue until cancellation is requested.
    /// </summary>
    /// <remarks>
    ///     When a handler throws, the message visibility is extended using exponential backoff derived from
    ///     <c>ApproximateReceiveCount</c>. When an entire received batch fails, poll backoff is applied before the next
    ///     <c>ReceiveMessage</c> call.
    /// </remarks>
    /// <param name="options">The consumer options for the active subscription.</param>
    /// <param name="handler">The handler invoked for each delivery.</param>
    /// <param name="cancellationToken">The token used to cancel the consume loop.</param>
    /// <returns>A task that completes when the consume loop stops.</returns>
    private async Task RunConsumeLoopAsync(
        TransportConsumerOptions options,
        Func<TransportMessage, CancellationToken, Task> handler,
        CancellationToken cancellationToken)
    {
        var maxMessages = options.PrefetchCount > 0 ? Math.Min(options.PrefetchCount, (ushort) 10) : (ushort) 1;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var receiveRequest = new ReceiveMessageRequest
                {
                    QueueUrl = options.Destination,
                    MaxNumberOfMessages = maxMessages,
                    WaitTimeSeconds = _options.LongPollWaitTimeSeconds,
                    VisibilityTimeout = _options.VisibilityTimeoutSeconds,
                    MessageAttributeNames = ["All"],
                    MessageSystemAttributeNames = ["ApproximateReceiveCount"]
                };

                var response = await _sqsClient.ReceiveMessageAsync(receiveRequest, cancellationToken)
                    .ConfigureAwait(false);

                if (response.Messages is null || response.Messages.Count == 0)
                {
                    _consecutiveFullBatchFailures = 0;
                    continue;
                }

                var batchFailed = true;

                foreach (var message in response.Messages)
                {
                    var requeueVisibilityTimeout =
                        SqsRequeueBackoff.ComputeRequeueVisibilityTimeout(message, _options);

                    var ackHandlers = new TransportConsumerAckHandlers
                    {
                        AckAsync = token => _sqsClient.DeleteMessageAsync(
                            new DeleteMessageRequest
                            {
                                QueueUrl = options.Destination,
                                ReceiptHandle = message.ReceiptHandle
                            },
                            token),
                        NackAsync = (requeue, token) => requeue
                            ? _sqsClient.ChangeMessageVisibilityAsync(
                                new ChangeMessageVisibilityRequest
                                {
                                    QueueUrl = options.Destination,
                                    ReceiptHandle = message.ReceiptHandle,
                                    VisibilityTimeout = requeueVisibilityTimeout
                                },
                                token)
                            : _sqsClient.DeleteMessageAsync(
                                new DeleteMessageRequest
                                {
                                    QueueUrl = options.Destination,
                                    ReceiptHandle = message.ReceiptHandle
                                },
                                token)
                    };

                    var transportMessage = SqsMessageMapper.ToTransportMessage(
                        message,
                        options.Destination,
                        ackHandlers);

                    try
                    {
                        await handler(transportMessage, cancellationToken).ConfigureAwait(false);
                        batchFailed = false;
                    }
                    catch (Exception) when (!cancellationToken.IsCancellationRequested)
                    {
                        await transportMessage.ReturnToQueueAsync(cancellationToken).ConfigureAwait(false);
                    }
                }

                if (batchFailed)
                {
                    _consecutiveFullBatchFailures++;
                    var pollBackoff = SqsRequeueBackoff.ComputePollBackoff(_consecutiveFullBatchFailures, _options);

                    if (pollBackoff > TimeSpan.Zero)
                    {
                        await Task.Delay(pollBackoff, cancellationToken).ConfigureAwait(false);
                    }
                }
                else
                {
                    _consecutiveFullBatchFailures = 0;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected during consumer shutdown.
        }
        finally
        {
            SignalStopped();
        }
    }

    /// <summary>
    ///     Creates a new task source used to observe consumer shutdown.
    /// </summary>
    /// <returns>The task source for the current consume session.</returns>
    private static TaskCompletionSource CreateStoppedTaskSource()
    {
        return new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>
    ///     Marks the current consume session as stopped.
    /// </summary>
    private void SignalStopped()
    {
        _stoppedTcs.TrySetResult();
    }
}