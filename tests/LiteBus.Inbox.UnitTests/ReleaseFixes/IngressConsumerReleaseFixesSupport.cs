using System.Reflection;
using LiteBus.Inbox.Abstractions;
using LiteBus.Messaging;
using LiteBus.Transport.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;

namespace LiteBus.Inbox.UnitTests.ReleaseFixes;

/// <summary>
///     Builds ingress consumers and deliveries for release regression tests.
/// </summary>
internal static class IngressConsumerReleaseFixesSupport
{
    /// <summary>
    ///     Creates an ingress consumer wired to a real handler and the supplied inbox.
    /// </summary>
    /// <param name="inbox">The inbox implementation that receives accepted deliveries.</param>
    /// <param name="options">The ingress destination and consumer settings.</param>
    /// <param name="messageConsumer">The optional transport consumer implementation.</param>
    /// <param name="hostOptions">The optional ingress host options.</param>
    /// <returns>The configured ingress consumer.</returns>
    internal static TransportInboxIngressConsumer CreateConsumer(
        IInbox inbox,
        TransportInboxIngressOptions options,
        IMessageConsumer? messageConsumer = null,
        TransportInboxIngressHostOptions? hostOptions = null)
    {
        var contractRegistry = new MessageContractRegistry();
        contractRegistry.Register<ProbeCommand>("probe.command");

        var handler = new TransportInboxIngressHandler(
            inbox,
            contractRegistry,
            new SystemTextJsonMessageSerializer(),
            options);

        return new TransportInboxIngressConsumer(
            messageConsumer ?? new FakeMessageConsumer(),
            handler,
            options,
            hostOptions ?? new TransportInboxIngressHostOptions { Enabled = false },
            NullLogger<TransportInboxIngressConsumer>.Instance);
    }

    /// <summary>
    ///     Creates a transport delivery for the registered probe contract.
    /// </summary>
    /// <param name="body">The delivery body bytes.</param>
    /// <param name="ack">The acknowledgement delegate.</param>
    /// <param name="nack">The negative-acknowledgement delegate.</param>
    /// <param name="contractName">The contract name header value.</param>
    /// <returns>A transport message for ingress release tests.</returns>
    internal static TransportMessage CreateMessage(
        byte[] body,
        Func<Task> ack,
        Func<bool, CancellationToken, Task> nack,
        string contractName = "probe.command")
    {
        var messageId = Guid.NewGuid().ToString("D");

        return new TransportMessage
        {
            Body = body,
            MessageId = messageId,
            Headers = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [TransportHeaders.ContractName] = contractName,
                [TransportHeaders.ContractVersion] = "1",
                [TransportHeaders.MessageId] = messageId
            },
            AckAsync = _ => ack(),
            NackAsync = nack
        };
    }

    /// <summary>
    ///     Invokes the private delivery handler on the ingress consumer.
    /// </summary>
    /// <param name="consumer">The ingress consumer under test.</param>
    /// <param name="message">The synthetic delivery.</param>
    /// <param name="cancellationToken">The token used to cancel acceptance.</param>
    /// <returns>A task that completes when the delivery is handled.</returns>
    internal static Task InvokeHandleDeliveryAsync(
        TransportInboxIngressConsumer consumer,
        TransportMessage message,
        CancellationToken cancellationToken = default)
    {
        var method = typeof(TransportInboxIngressConsumer).GetMethod(
                         "HandleDeliveryAsync",
                         BindingFlags.NonPublic | BindingFlags.Instance) ??
                     throw new InvalidOperationException("HandleDeliveryAsync not found.");

        return (Task) method.Invoke(consumer, [message, cancellationToken])!;
    }

    /// <summary>
    ///     Probe command type used by ingress release tests.
    /// </summary>
    /// <param name="Value">The probe payload value.</param>
    internal sealed record ProbeCommand(int Value);

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
