using LiteBus.Storage.PostgreSql.Exceptions;

namespace LiteBus.Storage.PostgreSql.UnitTests;

public sealed class PostgreSqlStorageExceptionTests
{
    [Fact]
    public void PostgreSqlStorageConfigurationException_exposes_message()
    {
        var exception = new PostgreSqlStorageConfigurationException("invalid script");

        exception.Message.Should().Be("invalid script");
    }

    [Fact]
    public void PostgreSqlStorageTimeoutException_exposes_message()
    {
        var exception = new PostgreSqlStorageTimeoutException("lock timed out");

        exception.Message.Should().Be("lock timed out");
    }
}
