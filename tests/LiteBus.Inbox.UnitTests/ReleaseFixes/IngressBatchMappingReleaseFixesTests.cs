using LiteBus.Inbox.Abstractions.Exceptions;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging;
using LiteBus.Transport.Abstractions;

namespace LiteBus.Inbox.UnitTests.ReleaseFixes;

/// <summary>
///     Verifies batch ingress reports header mapping failures as ingress exceptions.
/// </summary>
public sealed class IngressBatchMappingReleaseFixesTests
{
    /// <summary>
    ///     Verifies batch accept wraps header mapping failures like single accept does.
    /// </summary>
    /// <returns>A task that completes when the wrap assertion succeeds.</returns>
    [Fact]
    public async Task AcceptBatchAsync_WhenContractNameHeaderMissing_ShouldThrowInboxIngressException()
    {
        var handler = CreateHandler();

        var act = () => handler.AcceptBatchAsync([CreateMessage()]);

        var thrown = await act.Should().ThrowAsync<InboxIngressException>().ConfigureAwait(false);
        thrown.Which.InnerException.Should().BeOfType<TransportHeaderMappingException>();
    }

    /// <summary>
    ///     Verifies single accept still wraps header mapping failures.
    /// </summary>
    /// <returns>A task that completes when the wrap assertion succeeds.</returns>
    [Fact]
    public async Task AcceptAsync_WhenContractNameHeaderMissing_ShouldThrowInboxIngressException()
    {
        var handler = CreateHandler();

        var act = () => handler.AcceptAsync(CreateMessage());

        await act.Should().ThrowAsync<InboxIngressException>().ConfigureAwait(false);
    }

    /// <summary>
    ///     Creates an ingress handler wired to an in-memory inbox.
    /// </summary>
    /// <returns>The configured ingress handler.</returns>
    private static TransportInboxIngressHandler CreateHandler()
    {
        var contractRegistry = new MessageContractRegistry();
        contractRegistry.Register<ProbeCommand>("probe.command");

        var inbox = InboxWriterTestFactory.Create(
            new InMemoryInboxStore(),
            contractRegistry,
            new SystemTextJsonMessageSerializer(),
            TimeProvider.System);

        return new TransportInboxIngressHandler(
            inbox,
            contractRegistry,
            new SystemTextJsonMessageSerializer(),
            new TransportInboxIngressOptions
            {
                Safety = new TransportInboxIngressSafetyOptions
                {
                    RequireStableIdentity = false
                }
            });
    }

    /// <summary>
    ///     Creates a transport delivery without the required contract name header.
    /// </summary>
    /// <returns>A transport message missing its contract header.</returns>
    private static TransportMessage CreateMessage()
    {
        var messageId = Guid.NewGuid().ToString("D");

        return new TransportMessage
        {
            Body = """{"value":1}"""u8.ToArray(),
            MessageId = messageId,
            Headers = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [TransportHeaders.ContractVersion] = "1",
                [TransportHeaders.MessageId] = messageId
            },
            AckAsync = _ => Task.CompletedTask,
            NackAsync = (_, _) => Task.CompletedTask
        };
    }

    /// <summary>
    ///     Probe command type used by batch mapping release tests.
    /// </summary>
    /// <param name="Value">The probe payload value.</param>
    private sealed record ProbeCommand(int Value);
}
