using System;
using LiteBus.Outbox.Abstractions;
using LiteBus.Storage.PostgreSql;

namespace LiteBus.Outbox.Storage.PostgreSql;

/// <summary>
///     Resolves a bound <see cref="ITransactionalOutboxStore" /> from the ambient PostgreSQL transaction provider.
/// </summary>
public sealed class PostgreSqlTransactionalOutboxParticipant
{
    /// <summary>
    ///     Gets the PostgreSQL store registration used to create bound writer stores.
    /// </summary>
    private readonly PostgreSqlOutboxStoreRegistration _registration;

    /// <summary>
    ///     Gets the singleton outbox store used when immediate commit fallback is enabled.
    /// </summary>
    private readonly IOutboxStore _singletonStore;

    /// <summary>
    ///     Gets the optional ambient transaction provider supplied by the application.
    /// </summary>
    private readonly IPostgreSqlTransactionProvider? _transactionProvider;

    /// <summary>
    ///     Gets the transactional write mode configured on the PostgreSQL outbox module builder.
    /// </summary>
    private readonly TransactionalWriteMode _writeMode;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlTransactionalOutboxParticipant" /> class.
    /// </summary>
    /// <param name="registration">The PostgreSQL store registration.</param>
    /// <param name="singletonStore">The singleton outbox store registered for processors and auto-commit enqueue.</param>
    /// <param name="transactionProvider">The optional ambient transaction provider supplied by the application.</param>
    /// <param name="writeMode">The transactional write mode configured on the module builder.</param>
    public PostgreSqlTransactionalOutboxParticipant(
        PostgreSqlOutboxStoreRegistration registration,
        IOutboxStore singletonStore,
        IPostgreSqlTransactionProvider? transactionProvider,
        TransactionalWriteMode writeMode)
    {
        _registration = registration ?? throw new ArgumentNullException(nameof(registration));
        _singletonStore = singletonStore ?? throw new ArgumentNullException(nameof(singletonStore));
        _transactionProvider = transactionProvider;
        _writeMode = writeMode;
    }

    /// <summary>
    ///     Resolves the transactional outbox store for the current scope.
    /// </summary>
    /// <returns>The bound store when an ambient transaction is active, or the singleton store when fallback is enabled.</returns>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when <see cref="TransactionalWriteMode.RequireActiveTransaction" /> is configured and no ambient transaction
    ///     is active.
    /// </exception>
    public ITransactionalOutboxStore ResolveStore()
    {
        if (_transactionProvider?.TryGetCurrent(out var connection, out var transaction) == true)
        {
            return _registration.CreateTransactionalStore(connection!, transaction!);
        }

        if (_writeMode == TransactionalWriteMode.AllowImmediateCommit)
        {
            return (ITransactionalOutboxStore) _singletonStore;
        }

        throw new InvalidOperationException(
            "ITransactionalOutbox requires an active PostgreSQL transaction. " +
            "Activate IPostgreSqlTransactionProvider in the current scope (for example by opening BeginTransactionAsync " +
            "in unit-of-work middleware) or register AllowImmediateCommit only for development and tests.");
    }
}