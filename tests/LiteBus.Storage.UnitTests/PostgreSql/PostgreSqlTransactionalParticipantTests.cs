using LiteBus.Inbox.Storage.PostgreSql;
using LiteBus.Outbox.Storage.PostgreSql;
using Npgsql;

namespace LiteBus.Storage.UnitTests.PostgreSql;

/// <summary>
///     Verifies PostgreSQL transactional participants resolve bound stores from the ambient provider.
/// </summary>
public sealed class PostgreSqlTransactionalParticipantTests
{
    /// <summary>
    ///     Confirms require-active mode throws when the provider is inactive.
    /// </summary>
    [Fact]
    public void InboxParticipant_should_throw_when_provider_inactive_and_require_active()
    {
        var dataSource = NpgsqlDataSource.Create("Host=localhost;Database=test;Username=test;Password=test");
        var registration = new PostgreSqlInboxStoreRegistration(dataSource, new PostgreSqlInboxStoreOptions());
        var singleton = new PostgreSqlInboxStore(dataSource, new PostgreSqlInboxStoreOptions());

        var participant = new PostgreSqlTransactionalInboxParticipant(
            registration,
            singleton,
            null,
            TransactionalWriteMode.RequireActiveTransaction);

        var act = () => participant.ResolveStore();

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*requires an active PostgreSQL transaction*");
    }

    /// <summary>
    ///     Confirms allow-immediate mode falls back to the singleton store for inbox writes.
    /// </summary>
    [Fact]
    public void InboxParticipant_should_fallback_to_singleton_when_allow_immediate_commit()
    {
        var dataSource = NpgsqlDataSource.Create("Host=localhost;Database=test;Username=test;Password=test");
        var registration = new PostgreSqlInboxStoreRegistration(dataSource, new PostgreSqlInboxStoreOptions());
        var singleton = new PostgreSqlInboxStore(dataSource, new PostgreSqlInboxStoreOptions());

        var participant = new PostgreSqlTransactionalInboxParticipant(
            registration,
            singleton,
            null,
            TransactionalWriteMode.AllowImmediateCommit);

        participant.ResolveStore().Should().BeSameAs(singleton);
    }

    /// <summary>
    ///     Confirms allow-immediate mode falls back to the singleton store for outbox writes.
    /// </summary>
    [Fact]
    public void OutboxParticipant_should_fallback_to_singleton_when_allow_immediate_commit()
    {
        var dataSource = NpgsqlDataSource.Create("Host=localhost;Database=test;Username=test;Password=test");
        var registration = new PostgreSqlOutboxStoreRegistration(dataSource, new PostgreSqlOutboxStoreOptions());
        var singleton = new PostgreSqlOutboxStore(dataSource, new PostgreSqlOutboxStoreOptions());

        var participant = new PostgreSqlTransactionalOutboxParticipant(
            registration,
            singleton,
            null,
            TransactionalWriteMode.AllowImmediateCommit);

        participant.ResolveStore().Should().BeSameAs(singleton);
    }

    /// <summary>
    ///     Confirms require-active mode throws for outbox when the provider is inactive.
    /// </summary>
    [Fact]
    public void OutboxParticipant_should_throw_when_provider_inactive_and_require_active()
    {
        var dataSource = NpgsqlDataSource.Create("Host=localhost;Database=test;Username=test;Password=test");
        var registration = new PostgreSqlOutboxStoreRegistration(dataSource, new PostgreSqlOutboxStoreOptions());
        var singleton = new PostgreSqlOutboxStore(dataSource, new PostgreSqlOutboxStoreOptions());

        var participant = new PostgreSqlTransactionalOutboxParticipant(
            registration,
            singleton,
            null,
            TransactionalWriteMode.RequireActiveTransaction);

        var act = () => participant.ResolveStore();

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*requires an active PostgreSQL transaction*");
    }
}