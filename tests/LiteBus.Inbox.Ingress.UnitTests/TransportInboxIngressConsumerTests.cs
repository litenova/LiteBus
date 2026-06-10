using System.Reflection;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Abstractions.Exceptions;
using LiteBus.Inbox.Ingress;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Runtime.Abstractions;
using LiteBus.Transport.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;

namespace LiteBus.Inbox.Ingress.UnitTests;

/// <summary>
///     Verifies acknowledgement policy on <see cref="TransportInboxIngressConsumer" />.
/// </summary>
public sealed class TransportInboxIngressConsumerTests
{
    /// <summary>
    ///     Verifies successful acceptance acknowledges the delivery.
    /// </summary>
    /// <returns>A task that completes when the acknowledgement assertion succeeds.</returns>
    [Fact]
    public async Task HandleDeliveryAsync_WhenAcceptSucceeds_ShouldAcknowledge()
    {
        var ackCount = 0;
        var nackRequeue = new List<bool>();
        var consumer = CreateConsumer(requeueOnFailure: true);

        await InvokeHandleDeliveryAsync(
            consumer,
            CreateValidMessage(
                ack: () => { ackCount++; return Task.CompletedTask; },
                nack: (requeue, _) => { nackRequeue.Add(requeue); return Task.CompletedTask; }));

        ackCount.Should().Be(1);
        nackRequeue.Should().BeEmpty();
    }

    /// <summary>
    ///     Verifies store capacity failures negative-acknowledge without requeue.
    /// </summary>
    /// <returns>A task that completes when the discard assertion succeeds.</returns>
    [Fact]
    public async Task HandleDeliveryAsync_WhenStorageFull_ShouldDiscardWithoutRequeue()
    {
        var ackCount = 0;
        var nackRequeue = new List<bool>();
        var consumer = CreateConsumer(requeueOnFailure: true, inboxCapacity: 1);
        var handler = GetHandler(consumer);

        await handler.AcceptAsync(CreateValidMessage(
            ack: () => Task.CompletedTask,
            nack: (_, _) => Task.CompletedTask));

        await InvokeHandleDeliveryAsync(
            consumer,
            CreateValidMessage(
                ack: () => { ackCount++; return Task.CompletedTask; },
                nack: (requeue, _) => { nackRequeue.Add(requeue); return Task.CompletedTask; }));

        ackCount.Should().Be(0);
        nackRequeue.Should().ContainSingle().Which.Should().BeFalse();
    }

    /// <summary>
    ///     Verifies transient failures return the delivery to the queue when requeue is enabled.
    /// </summary>
    /// <returns>A task that completes when the requeue assertion succeeds.</returns>
    [Fact]
    public async Task HandleDeliveryAsync_WhenTransientFailureAndRequeueEnabled_ShouldReturnToQueue()
    {
        var ackCount = 0;
        var nackRequeue = new List<bool>();
        var throwingInbox = new ThrowingInbox(new IOException("transient"));
        var consumer = CreateConsumer(requeueOnFailure: true, inbox: throwingInbox);

        await InvokeHandleDeliveryAsync(
            consumer,
            CreateValidMessage(
                ack: () => { ackCount++; return Task.CompletedTask; },
                nack: (requeue, _) => { nackRequeue.Add(requeue); return Task.CompletedTask; }));

        ackCount.Should().Be(0);
        nackRequeue.Should().ContainSingle().Which.Should().BeTrue();
    }

    /// <summary>
    ///     Verifies acknowledgement failure after accept does not discard the broker delivery.
    /// </summary>
    /// <returns>A task that completes when the acknowledgement assertion succeeds.</returns>
    [Fact]
    public async Task HandleDeliveryAsync_WhenAckFailsAfterAccept_ShouldNotDiscard()
    {
        var nackRequeue = new List<bool>();
        var consumer = CreateConsumer(requeueOnFailure: true);

        await InvokeHandleDeliveryAsync(
            consumer,
            CreateValidMessage(
                ack: () => Task.FromException(new InvalidOperationException("ack failed")),
                nack: (requeue, _) => { nackRequeue.Add(requeue); return Task.CompletedTask; }));

        nackRequeue.Should().BeEmpty();
    }

    /// <summary>
    ///     Verifies batch buffering blocks additional deliveries until a flush releases admission slots.
    /// </summary>
    /// <returns>A task that completes when the backpressure assertion succeeds.</returns>
    [Fact]
    public async Task BatchAccept_WhenBufferFull_ShouldBlockUntilFlushCompletes()
    {
        var inbox = new SlowBatchInbox();
        var consumer = CreateConsumer(
            requeueOnFailure: true,
            inbox: inbox,
            options: new TransportInboxIngressOptions
            {
                PrefetchCount = 2,
                EnableBatchAccept = true,
                BatchMaxWait = TimeSpan.FromSeconds(30)
            });

        var first = InvokeHandleDeliveryAsync(
            consumer,
            CreateValidMessage(
                ack: () => Task.CompletedTask,
                nack: (_, _) => Task.CompletedTask));

        var second = InvokeHandleDeliveryAsync(
            consumer,
            CreateValidMessage(
                ack: () => Task.CompletedTask,
                nack: (_, _) => Task.CompletedTask));

        await Task.Delay(100);

        var thirdCompleted = false;
        var third = Task.Run(async () =>
        {
            await InvokeHandleDeliveryAsync(
                consumer,
                CreateValidMessage(
                    ack: () => Task.CompletedTask,
                    nack: (_, _) => Task.CompletedTask));
            thirdCompleted = true;
        });

        await Task.Delay(100);
        thirdCompleted.Should().BeFalse();

        inbox.ReleaseAccept();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));
        await third.WaitAsync(TimeSpan.FromSeconds(5));
        thirdCompleted.Should().BeTrue();
        inbox.BatchAcceptCount.Should().Be(1);
    }

    /// <summary>
    ///     Verifies partial batches flush after BatchMaxWait even when prefetch count is not reached.
    /// </summary>
    /// <returns>A task that completes when the batch flush assertion succeeds.</returns>
    [Fact]
    public async Task BatchAccept_ShouldFlushPartialBatchAfterBatchMaxWait()
    {
        var batchWait = TimeSpan.FromMilliseconds(150);
        var inbox = new RecordingInbox();
        var consumer = CreateConsumer(
            requeueOnFailure: true,
            inbox: inbox,
            options: new TransportInboxIngressOptions
            {
                PrefetchCount = 10,
                EnableBatchAccept = true,
                BatchMaxWait = batchWait
            });

        await InvokeHandleDeliveryAsync(consumer, CreateValidMessage(
            ack: () => Task.CompletedTask,
            nack: (_, _) => Task.CompletedTask));

        inbox.BatchAcceptCount.Should().Be(0);

        var deadline = DateTime.UtcNow + batchWait + TimeSpan.FromSeconds(2);
        while (DateTime.UtcNow < deadline && inbox.BatchAcceptCount == 0)
        {
            await Task.Delay(25);
        }

        inbox.BatchAcceptCount.Should().Be(1);
    }

    /// <summary>
    ///     Invokes the private delivery handler on the ingress consumer.
    /// </summary>
    /// <param name="consumer">The ingress consumer under test.</param>
    /// <param name="message">The synthetic delivery.</param>
    /// <returns>A task that completes when the delivery is handled.</returns>
    private static Task InvokeHandleDeliveryAsync(TransportInboxIngressConsumer consumer, TransportMessage message)
    {
        var method = typeof(TransportInboxIngressConsumer).GetMethod(
            "HandleDeliveryAsync",
            BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("HandleDeliveryAsync not found.");

        return (Task)method.Invoke(consumer, [message, CancellationToken.None])!;
    }

    /// <summary>
    ///     Gets the ingress handler wired into the consumer.
    /// </summary>
    /// <param name="consumer">The ingress consumer under test.</param>
    /// <returns>The handler instance.</returns>
    private static TransportInboxIngressHandler GetHandler(TransportInboxIngressConsumer consumer)
    {
        var field = typeof(TransportInboxIngressConsumer).GetField(
            "_handler",
            BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("_handler not found.");

        return (TransportInboxIngressHandler)field.GetValue(consumer)!;
    }

    /// <summary>
    ///     Creates an ingress consumer wired to a real handler and optional inbox store.
    /// </summary>
    /// <param name="requeueOnFailure">The requeue policy configured on ingress options.</param>
    /// <param name="inboxCapacity">The optional inbox capacity limit.</param>
    /// <param name="inbox">The optional inbox implementation.</param>
    /// <param name="options">The optional ingress options overriding defaults.</param>
    /// <returns>The configured ingress consumer.</returns>
    private static TransportInboxIngressConsumer CreateConsumer(
        bool requeueOnFailure,
        int? inboxCapacity = null,
        IInbox? inbox = null,
        TransportInboxIngressOptions? options = null)
    {
        var contractRegistry = new MessageContractRegistry();
        contractRegistry.Register<ProbeCommand>("probe.command", 1);

        inbox ??= new Inbox(
            new InMemoryInboxStore(new InMemoryInboxStoreOptions
            {
                Capacity = inboxCapacity ?? 100
            }),
            contractRegistry,
            new SystemTextJsonMessageSerializer(),
            TimeProvider.System);

        var handler = new TransportInboxIngressHandler(
            inbox,
            contractRegistry,
            new SystemTextJsonMessageSerializer());

        options ??= new TransportInboxIngressOptions { RequeueOnFailure = requeueOnFailure };

        return new TransportInboxIngressConsumer(
            new FakeMessageConsumer(),
            handler,
            options,
            new TransportInboxIngressHostOptions { Enabled = false },
            circuitBreaker: null,
            logger: NullLogger<TransportInboxIngressConsumer>.Instance);
    }

    /// <summary>
    ///     Creates a valid transport delivery for the registered probe contract.
    /// </summary>
    /// <param name="ack">The acknowledgement delegate.</param>
    /// <param name="nack">The negative-acknowledgement delegate.</param>
    /// <returns>A transport message for ingress consumer tests.</returns>
    private static TransportMessage CreateValidMessage(
        Func<Task> ack,
        Func<bool, CancellationToken, Task> nack) =>
        new()
        {
            Body = """{"value":1}"""u8.ToArray(),
            Headers = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [TransportHeaders.ContractName] = "probe.command",
                [TransportHeaders.ContractVersion] = "1",
                [TransportHeaders.MessageId] = Guid.NewGuid().ToString("D")
            },
            AckAsync = _ => ack(),
            NackAsync = nack
        };

    /// <summary>
    ///     Probe command type used by ingress consumer tests.
    /// </summary>
    private sealed record ProbeCommand(int Value);

    /// <summary>
    ///     Blocks batch accept until released so ingress backpressure can be observed.
    /// </summary>
    private sealed class SlowBatchInbox : IInbox
    {
        /// <summary>
        ///     Gets the gate that blocks <see cref="AcceptBatchAsync" /> until released.
        /// </summary>
        private readonly TaskCompletionSource _acceptGate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        ///     Gets the number of batch accept calls observed.
        /// </summary>
        public int BatchAcceptCount { get; private set; }

        /// <summary>
        ///     Releases the blocked batch accept call.
        /// </summary>
        public void ReleaseAccept() => _acceptGate.TrySetResult();

        /// <inheritdoc />
        public Task<InboxReceipt<TMessage>> AcceptAsync<TMessage>(
            TMessage message,
            InboxOptions? options = null,
            CancellationToken cancellationToken = default)
            where TMessage : notnull =>
            throw new NotSupportedException();

        /// <inheritdoc />
        public Task<InboxReceipt> AcceptAsync(
            object message,
            Type messageType,
            InboxOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        /// <inheritdoc />
        public Task<IReadOnlyList<InboxReceipt<TMessage>>> AcceptBatchAsync<TMessage>(
            IReadOnlyList<TMessage> messages,
            IReadOnlyList<InboxOptions?>? options = null,
            CancellationToken cancellationToken = default)
            where TMessage : notnull =>
            throw new NotSupportedException();

        /// <inheritdoc />
        public async Task<IReadOnlyList<InboxReceipt>> AcceptBatchAsync(
            IReadOnlyList<object> messages,
            IReadOnlyList<Type> messageTypes,
            IReadOnlyList<InboxOptions?>? options = null,
            CancellationToken cancellationToken = default)
        {
            await _acceptGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            BatchAcceptCount++;
            return [];
        }
    }

    /// <summary>
    ///     Records inbox batch accept calls.
    /// </summary>
    private sealed class RecordingInbox : IInbox
    {
        /// <summary>
        ///     Gets the number of batch accept calls observed.
        /// </summary>
        public int BatchAcceptCount { get; private set; }

        /// <inheritdoc />
        public Task<InboxReceipt<TMessage>> AcceptAsync<TMessage>(
            TMessage message,
            InboxOptions? options = null,
            CancellationToken cancellationToken = default)
            where TMessage : notnull =>
            throw new NotSupportedException();

        /// <inheritdoc />
        public Task<InboxReceipt> AcceptAsync(
            object message,
            Type messageType,
            InboxOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        /// <inheritdoc />
        public Task<IReadOnlyList<InboxReceipt<TMessage>>> AcceptBatchAsync<TMessage>(
            IReadOnlyList<TMessage> messages,
            IReadOnlyList<InboxOptions?>? options = null,
            CancellationToken cancellationToken = default)
            where TMessage : notnull =>
            throw new NotSupportedException();

        /// <inheritdoc />
        public Task<IReadOnlyList<InboxReceipt>> AcceptBatchAsync(
            IReadOnlyList<object> messages,
            IReadOnlyList<Type> messageTypes,
            IReadOnlyList<InboxOptions?>? options = null,
            CancellationToken cancellationToken = default)
        {
            BatchAcceptCount++;
            return Task.FromResult<IReadOnlyList<InboxReceipt>>([]);
        }
    }

    /// <summary>
    ///     Inbox test double that throws the configured exception on every accept call.
    /// </summary>
    private sealed class ThrowingInbox : IInbox
    {
        /// <summary>
        ///     Gets the exception thrown on accept.
        /// </summary>
        private readonly Exception _exception;

        /// <summary>
        ///     Initializes a new instance of the <see cref="ThrowingInbox" /> class.
        /// </summary>
        /// <param name="exception">The exception thrown on accept.</param>
        public ThrowingInbox(Exception exception)
        {
            _exception = exception;
        }

        /// <inheritdoc />
        public Task<InboxReceipt<TMessage>> AcceptAsync<TMessage>(
            TMessage message,
            InboxOptions? options = null,
            CancellationToken cancellationToken = default)
            where TMessage : notnull =>
            Task.FromException<InboxReceipt<TMessage>>(_exception);

        /// <inheritdoc />
        public Task<InboxReceipt> AcceptAsync(
            object message,
            Type messageType,
            InboxOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromException<InboxReceipt>(_exception);

        /// <inheritdoc />
        public Task<IReadOnlyList<InboxReceipt<TMessage>>> AcceptBatchAsync<TMessage>(
            IReadOnlyList<TMessage> messages,
            IReadOnlyList<InboxOptions?>? options = null,
            CancellationToken cancellationToken = default)
            where TMessage : notnull =>
            Task.FromException<IReadOnlyList<InboxReceipt<TMessage>>>(_exception);

        /// <inheritdoc />
        public Task<IReadOnlyList<InboxReceipt>> AcceptBatchAsync(
            IReadOnlyList<object> messages,
            IReadOnlyList<Type> messageTypes,
            IReadOnlyList<InboxOptions?>? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromException<IReadOnlyList<InboxReceipt>>(_exception);
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
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        /// <inheritdoc />
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        /// <inheritdoc />
        public Task WaitUntilStoppedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        /// <inheritdoc />
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
