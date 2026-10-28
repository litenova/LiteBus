using LiteBus.Inbox.Storage.PostgreSql;
using LiteBus.Outbox.Storage.PostgreSql;
using LiteBus.Storage.PostgreSql.Stores;

namespace LiteBus.Storage.UnitTests.PostgreSql;

public sealed class PostgreSqlWorkSignalListenCommandTests
{
    [Fact]
    public void BuildListenCommand_WithSpacedChannel_ShouldQuoteIdentifier()
    {
        PostgreSqlWorkSignal.BuildListenCommand("my channel").Should().Be("LISTEN \"my channel\"");
    }

    [Fact]
    public void BuildListenCommand_WithInboxChannel_ShouldMatchNotifyChannel()
    {
        PostgreSqlWorkSignal.BuildListenCommand(PostgreSqlInboxNotifyChannel.ChannelName)
            .Should().Be("LISTEN \"litebus_inbox_work\"");
    }

    [Fact]
    public void BuildListenCommand_WithOutboxChannel_ShouldMatchNotifyChannel()
    {
        PostgreSqlWorkSignal.BuildListenCommand(PostgreSqlOutboxNotifyChannel.ChannelName)
            .Should().Be("LISTEN \"litebus_outbox_work\"");
    }

    [Fact]
    public void BuildListenCommand_WithEmbeddedQuote_ShouldDoubleQuote()
    {
        PostgreSqlWorkSignal.BuildListenCommand("a\"b").Should().Be("LISTEN \"a\"\"b\"");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildListenCommand_WithBlankChannel_ShouldThrow(string? channelName)
    {
        var act = () => PostgreSqlWorkSignal.BuildListenCommand(channelName!);

        act.Should().Throw<ArgumentException>();
    }
}
