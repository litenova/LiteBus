using System.Text;
using AwesomeAssertions;
using LiteBus.Transport.Abstractions;

namespace LiteBus.Transport.UnitTests;

/// <summary>
///     Verifies transport header value parsing helpers.
/// </summary>
public sealed class TransportHeaderValuesTests
{
    /// <summary>
    ///     Verifies byte-encoded header values deserialize to strings.
    /// </summary>
    [Fact]
    public void GetString_ShouldReadByteEncodedHeaders()
    {
        var headers = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [TransportHeaders.CorrelationId] = Encoding.UTF8.GetBytes("bytes-correlation"),
            [TransportHeaders.TenantId] = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("memory-tenant"))
        };

        TransportHeaderValues.GetString(headers, TransportHeaders.CorrelationId).Should().Be("bytes-correlation");
        TransportHeaderValues.GetString(headers, TransportHeaders.TenantId).Should().Be("memory-tenant");
    }
}