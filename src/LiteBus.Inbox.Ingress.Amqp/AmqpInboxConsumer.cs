using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Transport.Amqp;
using LiteBus.Messaging.Abstractions;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Inbox.Ingress.Amqp;

/// <summary>
///     Consumes AMQP messages and accepts them into the inbox store as LiteBus background service work.
/// </summary>
public sealed class AmqpInboxConsumer : IBackgroundService
{
    /// <summary>
    ///     Gets the AMQP consumer used to subscribe to the ingress queue.
    /// </summary>
    private readonly IAmqpConsumer _consumer;

    /// <summary>
    ///     Gets the handler that maps deliveries to <see cref="Abstractions.IInbox.AcceptAsync" />.
    /// </summary>
    private readonly AmqpInboxIngressHandler _handler;

    /// <summary>
    ///     Gets the hosting options that control whether the ingress loop is enabled.
    /// </summary>
    private readonly AmqpInboxIngressHostOptions _hostOptions;

    /// <summary>
    ///     Gets the ingress queue and broker settings.
    /// </summary>
    private readonly AmqpInboxIngressOptions _options;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AmqpInboxConsumer" /> class.
    /// </summary>
    /// <param name="consumer">The AMQP consumer used to subscribe to the ingress queue.</param>
    /// <param name="handler">The handler that maps deliveries to inbox acceptance.</param>
    /// <param name="options">The ingress queue and broker settings.</param>
    /// <param name="hostOptions">The hosting options that control whether the ingress loop is enabled.</param>
    public AmqpInboxConsumer(
        IAmqpConsumer consumer,
        AmqpInboxIngressHandler handler,
        AmqpInboxIngressOptions options,
        AmqpInboxIngressHostOptions hostOptions)
    {
        _consumer = consumer ?? throw new ArgumentNullException(nameof(consumer));
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _hostOptions = hostOptions ?? throw new ArgumentNullException(nameof(hostOptions));
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_hostOptions.Enabled)
        {
            return;
        }

        var consumerOptions = new AmqpConsumerOptions
        {
            QueueName = _options.QueueName,
            PrefetchCount = _options.PrefetchCount,
            DeclareQueue = _options.DeclareQueue,
            DurableQueue = _options.DurableQueue
        };

        await _consumer
            .StartAsync(consumerOptions, HandleDeliveryAsync, stoppingToken)
            .ConfigureAwait(false);

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            await _consumer.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Accepts one AMQP delivery into the inbox and acknowledges the broker delivery.
    /// </summary>
    /// <param name="message">The received AMQP delivery.</param>
    /// <param name="cancellationToken">The token used to cancel acceptance.</param>
    /// <returns>A task that completes when the delivery has been acknowledged.</returns>
    private async Task HandleDeliveryAsync(AmqpReceivedMessage message, CancellationToken cancellationToken)
    {
        try
        {
            await _handler.AcceptAsync(message, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (ShouldRequeue(exception))
        {
            await message.ReturnToQueueAsync(cancellationToken).ConfigureAwait(false);
            return;
        }
        catch (Exception)
        {
            await message.DiscardAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            await message.AcceptAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await message.DiscardAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Determines whether a failed delivery should be requeued for retry.
    /// </summary>
    /// <param name="exception">The exception thrown while accepting the delivery.</param>
    /// <returns><see langword="true" /> when the broker should requeue the message; otherwise <see langword="false" />.</returns>
    private bool ShouldRequeue(Exception exception)
    {
        if (!_options.RequeueOnFailure)
        {
            return false;
        }

        exception = UnwrapException(exception);

        return exception is not (
            MessageContractNotRegisteredException
            or InvalidOperationException
            or ArgumentException
            or FormatException
            or System.Text.Json.JsonException);
    }

    /// <summary>
    ///     Unwraps reflection and aggregate wrappers so acknowledgement policy inspects the root failure.
    /// </summary>
    /// <param name="exception">The exception observed by the consumer.</param>
    /// <returns>The root exception thrown by inbox acceptance.</returns>
    private static Exception UnwrapException(Exception exception)
    {
        while (true)
        {
            switch (exception)
            {
                case TargetInvocationException target when target.InnerException is not null:
                    exception = target.InnerException;
                    continue;
                case AggregateException aggregate when aggregate.InnerExceptions.Count == 1:
                    exception = aggregate.InnerExceptions[0];
                    continue;
                default:
                    return exception;
            }
        }
    }
}
