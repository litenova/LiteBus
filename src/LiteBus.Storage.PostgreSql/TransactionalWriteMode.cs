namespace LiteBus.Storage.PostgreSql;

/// <summary>
///     Controls how scoped <see cref="LiteBus.Inbox.Abstractions.ITransactionalInbox" /> and
///     <see cref="LiteBus.Outbox.Abstractions.ITransactionalOutbox" /> behave when no ambient PostgreSQL transaction is active.
/// </summary>
public enum TransactionalWriteMode
{
    /// <summary>
    ///     Throws when <see cref="IPostgreSqlTransactionProvider.TryGetCurrent" /> returns <see langword="false" />.
    ///     This is the default for PostgreSQL ambient registration.
    /// </summary>
    RequireActiveTransaction = 0,

    /// <summary>
    ///     Falls back to the singleton auto-commit store when no ambient transaction is active. Intended for development and
    ///     tests only; do not use in production command handlers that require atomic domain and messaging writes.
    /// </summary>
    AllowImmediateCommit = 1
}
