using System.Text;
using LiteBus.Transport.Amqp;

namespace LiteBus.Transport.Amqp.IntegrationTests;

public sealed class AmqpHeaderValuesTests
{
    [Fact]
    public void GetString_ShouldConvertSupportedHeaderValueTypes()
    {
        var headers = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["text"] = "plain",
            ["bytes"] = Encoding.UTF8.GetBytes("byte-text"),
            ["readonly"] = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("readonly-text")),
            ["memory"] = new Memory<byte>(Encoding.UTF8.GetBytes("memory-text")),
            ["number"] = 42
        };

        AmqpHeaderValues.GetString(headers, "text").Should().Be("plain");
        AmqpHeaderValues.GetString(headers, "bytes").Should().Be("byte-text");
        AmqpHeaderValues.GetString(headers, "readonly").Should().Be("readonly-text");
        AmqpHeaderValues.GetString(headers, "memory").Should().Be("memory-text");
        AmqpHeaderValues.GetString(headers, "number").Should().Be("42");
        AmqpHeaderValues.GetString(headers, "missing").Should().BeNull();
    }

    [Fact]
    public void GetString_WhenHeadersNull_ShouldThrow()
    {
        var act = () => AmqpHeaderValues.GetString(null!, "name");

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void GetInt32_ShouldParseNumericHeaderVariants()
    {
        var headers = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["int"] = 7,
            ["byte"] = (byte)3,
            ["sbyte"] = (sbyte)-2,
            ["short"] = (short)11,
            ["long"] = 15L,
            ["text"] = "21",
            ["bytes-one"] = new byte[] { 9 },
            ["bytes-four"] = BitConverter.GetBytes(1234)
        };

        AmqpHeaderValues.GetInt32(headers, "int").Should().Be(7);
        AmqpHeaderValues.GetInt32(headers, "byte").Should().Be(3);
        AmqpHeaderValues.GetInt32(headers, "sbyte").Should().Be(-2);
        AmqpHeaderValues.GetInt32(headers, "short").Should().Be(11);
        AmqpHeaderValues.GetInt32(headers, "long").Should().Be(15);
        AmqpHeaderValues.GetInt32(headers, "text").Should().Be(21);
        AmqpHeaderValues.GetInt32(headers, "bytes-one").Should().Be(9);
        AmqpHeaderValues.GetInt32(headers, "bytes-four").Should().Be(1234);
        AmqpHeaderValues.GetInt32(headers, "missing").Should().BeNull();
        AmqpHeaderValues.GetInt32(headers, "text").Should().Be(21);
    }

    [Fact]
    public void GetInt32_WhenValueNotNumeric_ShouldReturnNull()
    {
        var headers = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["bad-text"] = "not-a-number",
            ["bad-bytes"] = new byte[] { 1, 2, 3 }
        };

        AmqpHeaderValues.GetInt32(headers, "bad-text").Should().BeNull();
        AmqpHeaderValues.GetInt32(headers, "bad-bytes").Should().BeNull();
    }
}
