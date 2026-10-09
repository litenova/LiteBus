using System;
using System.Threading;
using System.Threading.Tasks;
using Amazon.SQS;
using Amazon.SQS.Model;
using LiteBus.Transport.Abstractions;

namespace LiteBus.Transport.AwsSqs;

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
    ///     Tracks whether asynchronous disposal has started.
    /// </summary>
    private int _disposed;

    /// <summary>
    ///     Gets the transport options controlling poll and visibility behavior.
    /// </summary>
    private readonly AwsSqsTransportOptions _options;

    /// <summary>
    ///     Gets the SQS client used to receive and acknowledge messages.
    /// </summary>
    private readonly IAmazonSQS _sqsClient;

    /// <summary>
    ///     Gets the time source used for visibility extension delays while handlers run.
    /// </summary>
    private readonly TimeProvider _timeProvider;

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
    /// <param name="timeProvider">The time source used for visibility extension delays while handlers run.</param>
    public SqsConsumer(IAmazonSQS sqsClient, AwsSqsTransportOptions options, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(sqsClient);
        ArgumentNullException.ThrowIfNull(options);
        _sqsClient = sqsClient;
        _options = options;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async Task StartAsync(
        TransportConsumerOptions options,
        Func<TransportMessage, CancellationToken, Task> handler,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Destination);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.ReceiveBatchSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.ReceiveBatchSize, 10);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxInFlightMessages, 1);

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

            var consumeCts = _consumeCts;

            try
            {
                await consumeCts.CancelAsync().ConfigureAwait(false);

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
            }
            finally
            {
                consumeCts.Dispose();
                _consumeCts = null;
                _consumeTask = null;
                SignalStopped();
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task WaitUntilStoppedAsync(CancellationToken cancellationToken = default)
    {
        if (_consumeTask is { } consumeTask)
        {
            await consumeTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        await _stoppedTcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await StopAsync().ConfigureAwait(false);
        _lifecycleGate.Dispose();
    }

    /// <summary>
    ///     Invokes the consumer handler while extending SQS visibility so a long handler keeps its receipt handle alive.
    /// </summary>
    /// <remarks>
    ///     One <c>ChangeMessageVisibility</c> is issued per half visibility timeout through the configured
    ///     <see cref="TimeProvider" />. The requeue delay computed at receive time is unchanged and still applies
    ///     when the handler requests requeue.
    /// </remarks>
    /// <param name="handler">The bounded handler invoked for the delivery.</param>
    /// <param name="transportMessage">The transport delivery passed to the handler.</param>
    /// <param name="brokerMessage">The received SQS message supplying the receipt handle.</param>
    /// <param name="queueUrl">The queue URL owning the receipt handle.</param>
    /// <param name="cancellationToken">The token used to cancel handler execution.</param>
    /// <returns>The settlement outcome observed by the caller.</returns>
    private async Task<TransportConsumerInvocationOutcome> InvokeHandlerWithVisibilityExtensionAsync(
        Func<TransportMessage, CancellationToken, Task<TransportConsumerInvocationOutcome>> handler,
        TransportMessage transportMessage,
        Message brokerMessage,
        string queueUrl,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(transportMessage);
        ArgumentNullException.ThrowIfNull(brokerMessage);
        ArgumentException.ThrowIfNullOrWhiteSpace(queueUrl);

        return await SqsVisibilityHeartbeat.RunWithVisibilityExtensionAsync(
                token => _sqsClient.ChangeMessageVisibilityAsync(
                    new ChangeMessageVisibilityRequest
                    {
                        QueueUrl = queueUrl,
                        ReceiptHandle = brokerMessage.ReceiptHandle,
                        VisibilityTimeout = _options.VisibilityTimeoutSeconds
                    },
                    token),
                SqsVisibilityHeartbeat.ComputeExtensionInterval(_options.VisibilityTimeoutSeconds),
                _timeProvider,
                handlerToken => handler(transportMessage, handlerToken),
                cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Long-polls the configured queue until cancellation is requested.
    /// </summary>
    /// <remarks>
    ///     While a handler runs, message visibility is extended once per half visibility timeout so the receipt
    ///     handle cannot expire mid-handler. When a handler throws, the message visibility is extended using
    ///     exponential backoff derived from <c>ApproximateReceiveCount</c>. When an entire received batch fails,
    ///     poll backoff is applied before the next <c>ReceiveMessage</c> call.
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
        var boundedHandler = TransportConsumerHandlerInvoker.CreateBoundedOutcomeHandler(
            handler,
            options.MaxInFlightMessages);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var receiveRequest = new ReceiveMessageRequest
                {
                    QueueUrl = options.Destination,
                    MaxNumberOfMessages = options.ReceiveBatchSize,
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

                    var outcome = await InvokeHandlerWithVisibilityExtensionAsync(
                            boundedHandler,
                            transportMessage,
                            message,
                            options.Destination,
                            cancellationToken)
                        .ConfigureAwait(false);

                    if (outcome == TransportConsumerInvocationOutcome.Handled)
                    {
                        batchFailed = false;
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
