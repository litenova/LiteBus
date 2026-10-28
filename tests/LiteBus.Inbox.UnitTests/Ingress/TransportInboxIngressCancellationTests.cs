using System.Reflection;
using LiteBus.Inbox.Abstractions;
using LiteBus.Messaging;
using LiteBus.Transport.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;

namespace LiteBus.Inbox.UnitTests.Ingress;

/// <summary>
///     Verifies cancelled ingress acceptance leaves deliveries unacknowledged instead of discarding them.
/// </summary>
public sealed class TransportInboxIngressCancellationTests
{
    /// <summary>
    ///     Verifies a cancelled single accept propagates without acknowledging or discarding the delivery.
    /// </summary>
    /// <returns>A task that completes when the settlement assertion succeeds.</returns>
    [Fact]
    public async Task HandleDeliveryAsync_WhenAcceptCancelled_ShouldPropagateWithoutSettling()
    {
        var ackCount = 0;
        var nackRequeue = new List<bool>();
        var consumer = CreateConsumer(new CancellingInbox());

        using var cancellationSource = new CancellationTokenSource();
        await cancellationSource.CancelAsync().ConfigureAwait(false);

        var act = () => InvokeHandleDeliveryAsync(
            consumer,
            CreateMessage(
                () =>
                {
                    ackCount++;
                    return Task.CompletedTask;
                },
                (requeue, _) =>
                {
                    nackRequeue.Add(requeue);
                    return Task.CompletedTask;
                }),
            cancellationSource.Token);

        await act.Should().ThrowAsync<OperationCanceledException>().ConfigureAwait(false);
        ackCount.Should().Be(0);
        nackRequeue.Should().BeEmpty();
    }

    /// <summary>
    ///     Verifies a cancelled batch accept releases admission and rethrows without committing any delivery.
    /// </summary>
    /// <returns>A task that completes when the settlement assertion succeeds.</returns>
    [Fact]
    public async Task AcceptBatchAsync_WhenBatchAcceptCancelled_ShouldReleaseAdmissionAndRethrow()
    {
        var ackCount = 0;
        var commitCount = 0;
        var consumer = CreateConsumer(
            new CancellingInbox(),
            new TransportInboxIngressOptions
            {
                RequeueOnFailure = true,
                Safety = new TransportInboxIngressSafetyOptions
                {
                    BatchSize = 2,
                    EnableBatchAccept = true,
                    RequireStableIdentity = false
                }
            });

        var admission = GetBatchAdmission(consumer);
        await admission.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        await admission.WaitAsync(CancellationToken.None).ConfigureAwait(false);

        var messages = new List<TransportMessage>
        {
            CreateMessage(
                () =>
                {
                    ackCount++;
                    return Task.CompletedTask;
                },
                (requeue, _) =>
                {
                    if (!requeue)
                    {
                        commitCount++;
                    }

                    return Task.CompletedTask;
                }),
            CreateMessage(
                () =>
                {
                    ackCount++;
                    return Task.CompletedTask;
                },
                (requeue, _) =>
                {
                    if (!requeue)
                    {
                        commitCount++;
                    }

                    return Task.CompletedTask;
                })
        };

        using var cancellationSource = new CancellationTokenSource();
        await cancellationSource.CancelAsync().ConfigureAwait(false);

        var act = () => InvokeAcceptBatchAsync(consumer, messages, cancellationSource.Token);

        await act.Should().ThrowAsync<OperationCanceledException>().ConfigureAwait(false);
        ackCount.Should().Be(0);
        commitCount.Should().Be(0);
        admission.CurrentCount.Should().Be(2);
    }

    /// <summary>
    ///     Invokes the private delivery handler on the ingress consumer.
    /// </summary>
    /// <param name="consumer">The ingress consumer under test.</param>
    /// <param name="message">The synthetic delivery.</param>
    /// <param name="cancellationToken">The token used to cancel acceptance.</param>
    /// <returns>A task that completes when the delivery is handled.</returns>
    private static Task InvokeHandleDeliveryAsync(
        TransportInboxIngressConsumer consumer,
        TransportMessage message,
        CancellationToken cancellationToken)
    {
        var method = typeof(TransportInboxIngressConsumer).GetMethod(
                         "HandleDeliveryAsync",
                         BindingFlags.NonPublic | BindingFlags.Instance) ??
                     throw new InvalidOperationException("HandleDeliveryAsync not found.");

        return (Task) method.Invoke(consumer, [message, cancellationToken])!;
    }

    /// <summary>
    ///     Invokes the private batch accept handler on the ingress consumer.
    /// </summary>
    /// <param name="consumer">The ingress consumer under test.</param>
    /// <param name="messages">The buffered deliveries.</param>
    /// <param name="cancellationToken">The token used to cancel acceptance.</param>
    /// <returns>A task that completes when the batch is handled.</returns>
    private static Task InvokeAcceptBatchAsync(
        TransportInboxIngressConsumer consumer,
        List<TransportMessage> messages,
        CancellationToken cancellationToken)
    {
        var method = typeof(TransportInboxIngressConsumer).GetMethod(
                         "AcceptAndAcknowledgeBatchAsync",
                         BindingFlags.NonPublic | BindingFlags.Instance) ??
                     throw new InvalidOperationException("AcceptAndAcknowledgeBatchAsync not found.");

        return (Task) method.Invoke(consumer, [messages, cancellationToken])!;
    }

    /// <summary>
    ///     Gets the batch admission gate wired into the consumer.
    /// </summary>
    /// <param name="consumer">The ingress consumer under test.</param>
    /// <returns>The admission gate.</returns>
    private static SemaphoreSlim GetBatchAdmission(TransportInboxIngressConsumer consumer)
    {
        var field = typeof(TransportInboxIngressConsumer).GetField(
                        "_batchAdmission",
                        BindingFlags.NonPublic | BindingFlags.Instance) ??
                    throw new InvalidOperationException("_batchAdmission not found.");

        return (SemaphoreSlim) field.GetValue(consumer)!;
    }

    /// <summary>
    ///     Creates an ingress consumer wired to a real handler and the supplied inbox.
    /// </summary>
    /// <param name="inbox">The inbox implementation.</param>
    /// <param name="options">The optional ingress options overriding defaults.</param>
    /// <returns>The configured ingress consumer.</returns>
    private static TransportInboxIngressConsumer CreateConsumer(
        IInbox inbox,
        TransportInboxIngressOptions? options = null)
    {
        var contractRegistry = new MessageContractRegistry();
        contractRegistry.Register<ProbeCommand>("probe.command");

        options ??= new TransportInboxIngressOptions
        {
            RequeueOnFailure = true,
            Safety = new TransportInboxIngressSafetyOptions
            {
                RequireStableIdentity = false
            }
        };

        var handler = new TransportInboxIngressHandler(
            inbox,
            contractRegistry,
            new SystemTextJsonMessageSerializer(),
            options);

        return new TransportInboxIngressConsumer(
            new FakeMessageConsumer(),
            handler,
            options,
            new TransportInboxIngressHostOptions { Enabled = false },
            NullLogger<TransportInboxIngressConsumer>.Instance);
    }

    /// <summary>
    ///     Creates a valid transport delivery for the registered probe contract.
    /// </summary>
    /// <param name="ack">The acknowledgement delegate.</param>
    /// <param name="nack">The negative-acknowledgement delegate.</param>
    /// <returns>A transport message for ingress cancellation tests.</returns>
    private static TransportMessage CreateMessage(
        Func<Task> ack,
        Func<bool, CancellationToken, Task> nack)
    {
        var messageId = Guid.NewGuid().ToString("D");

        return new TransportMessage
        {
            Body = """{"value":1}"""u8.ToArray(),
            MessageId = messageId,
            Headers = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [TransportHeaders.ContractName] = "probe.command",
                [TransportHeaders.ContractVersion] = "1",
                [TransportHeaders.MessageId] = messageId
            },
            AckAsync = _ => ack(),
            NackAsync = nack
        };
    }

    /// <summary>
    ///     Probe command type used by ingress cancellation tests.
    /// </summary>
    private sealed record ProbeCommand(int Value);

    /// <summary>
    ///     Inbox test double that observes cancellation on every accept call.
    /// </summary>
    private sealed class CancellingInbox : IInbox
    {
        /// <inheritdoc />
        public Task<InboxReceipt<TMessage>> AcceptAsync<TMessage>(
            InboxAcceptItem<TMessage> item,
            CancellationToken cancellationToken = default)
            where TMessage : notnull
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc />
        public Task<InboxReceipt> AcceptAsync(
            InboxAcceptItem item,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(new InboxReceipt
            {
                Id = Guid.NewGuid(),
                MessageType = item.Message.GetType(),
                Contract = new MessageContractReference { Name = "probe.command", Version = 1 },
                AcceptedAt = DateTimeOffset.UtcNow,
                Trace = MessageTrace.None.Instance,
                Tenant = TenantScope.Unscoped.Instance
            });
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<InboxReceipt>> AcceptBatchAsync(
            IReadOnlyList<InboxAcceptItem> items,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<InboxReceipt>>([]);
        }
    }

    /// <summary>
    ///     Fake transport consumer satisfying ingress consumer dependencies.
    /// </summary>
    private sealed class FakeMessageConsumer : IMessageConsumer
    {
        /// <inheritdoc />
        public Task StartAsync(
            TransportConsumerOptions options,
            Func<TransportMessage, CancellationToken, Task> handler,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task WaitUntilStoppedAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
