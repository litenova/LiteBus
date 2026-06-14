using LiteBus.Storage.PostgreSql;
using LiteBus.Inbox;
using LiteBus.Outbox;
using LiteBus.Messaging;

namespace LiteBus.Storage.IntegrationTests.PostgreSql;

public sealed class PostgreSqlAdvisoryLockScopeTests
{
    [Fact]
    public void CreateLockKeys_ShouldUseIndependentHashesForEachPart()
    {
        var (key1, key2) = PostgreSqlAdvisoryLockScope.CreateLockKeys("litebus:schema:bootstrap");

        key1.Should().BePositive();
        key2.Should().BePositive();
        key1.Should().NotBe(key2);
    }

    [Fact]
    public void CreateLockKeys_ShouldBeDeterministicForSameInput()
    {
        var first = PostgreSqlAdvisoryLockScope.CreateLockKeys("litebus:inbox:public:table");
        var second = PostgreSqlAdvisoryLockScope.CreateLockKeys("litebus:inbox:public:table");

        first.Should().Be(second);
    }

    [Fact]
    public void CreateLockKeys_ShouldDifferForDifferentInputs()
    {
        var first = PostgreSqlAdvisoryLockScope.CreateLockKeys("litebus:inbox:a");
        var second = PostgreSqlAdvisoryLockScope.CreateLockKeys("litebus:inbox:b");

        first.Should().NotBe(second);
    }
}