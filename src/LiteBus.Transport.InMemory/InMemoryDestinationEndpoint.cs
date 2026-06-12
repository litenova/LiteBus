using System.Threading.Channels;

namespace LiteBus.Transport.InMemory;

/// <summary>
///     One destination endpoint backed by an unbounded channel.
/// </summary>
internal sealed class InMemoryDestinationEndpoint
{
    /// <summary>
    ///     Gets the channel carrying pending deliveries for the destination.
    /// </summary>
    private readonly Channel<InMemoryPendingDelivery> _channel = Channel.CreateUnbounded<InMemoryPendingDelivery>(
        new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });

    /// <summary>
    ///     Gets the writer used by publishers.
    /// </summary>
    /// <returns>The channel writer for the destination.</returns>
    internal ChannelWriter<InMemoryPendingDelivery> Writer => _channel.Writer;

    /// <summary>
    ///     Gets the reader used by consumers.
    /// </summary>
    /// <returns>The channel reader for the destination.</returns>
    internal ChannelReader<InMemoryPendingDelivery> Reader => _channel.Reader;
}
