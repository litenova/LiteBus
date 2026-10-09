using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Storage.UnitTests.PostgreSql;

public sealed class PostgreSqlSchemaVersionConversionTests
{
    [Theory]
    [InlineData(3, 3)]
    [InlineData(7L, 7)]
    [InlineData((short)2, 2)]
    public void ConvertVersionResult_WithSupportedTypes_ShouldConvert(object result, int expected)
    {
        PostgreSqlSchemaVersionStore.ConvertVersionResult(result).Should().Be(expected);
    }

    [Fact]
    public void ConvertVersionResult_WithWholeDecimal_ShouldConvert()
    {
        PostgreSqlSchemaVersionStore.ConvertVersionResult(5.0m).Should().Be(5);
    }

    [Fact]
    public void ConvertVersionResult_WithOverflowingDecimal_ShouldThrowOverflowException()
    {
        var act = () => PostgreSqlSchemaVersionStore.ConvertVersionResult(9999999999.0m);

        act.Should().Throw<OverflowException>();
    }

    [Fact]
    public void ConvertVersionResult_WithNull_ShouldReturnZero()
    {
        PostgreSqlSchemaVersionStore.ConvertVersionResult(null).Should().Be(0);
    }

    [Theory]
    [InlineData(2.5)]
    [InlineData(1.5f)]
    [InlineData("3")]
    [InlineData(true)]
    [InlineData((byte)3)]
    public void ConvertVersionResult_WithUnsupportedTypes_ShouldThrow(object result)
    {
        var act = () => PostgreSqlSchemaVersionStore.ConvertVersionResult(result);

        act.Should().Throw<DurableStorageConfigurationException>();
    }

    [Theory]
    [InlineData(long.MaxValue)]
    public void ConvertVersionResult_WithOverflowingValues_ShouldThrowOverflowException(object result)
    {
        var act = () => PostgreSqlSchemaVersionStore.ConvertVersionResult(result);

        act.Should().Throw<OverflowException>();
    }
}
