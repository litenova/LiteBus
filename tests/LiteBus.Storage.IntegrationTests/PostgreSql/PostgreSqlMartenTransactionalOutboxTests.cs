using System.Diagnostics.CodeAnalysis;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Outbox;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.PostgreSql;
using LiteBus.Storage.PostgreSql;
using Marten;
using Marten.Services;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LiteBus.Storage.IntegrationTests.PostgreSql;

/// <summary>
///     Verifies that a Marten session and the transactional outbox commit and roll back together when both are bound
///     to the connection and transaction the application's unit of work owns.
/// </summary>
/// <remarks>
///     <para>
///         This is the pattern the transactional-writes guide documents for Marten, Dapper, and raw ADO.NET, and it
///         was documented without a fixture for long enough that a consumer porting a Marten application read the
///         docs, concluded the path did not exist, and hand-wrote inserts against the outbox table instead. The claim
///         is now tested.
///     </para>
///     <para>
///         The application's unit of work owns the transaction. Marten joins it through
///         <see cref="SessionOptions" />, which is what keeps the read path out of it: a session used for queries is
///         resolved separately and never opens a write transaction.
///     </para>
/// </remarks>
public sealed class PostgreSqlMartenTransactionalOutboxTests : IClassFixture<PostgreSqlFixture>
{
    /// <summary>
    ///     The PostgreSQL fixture shared across tests.
    /// </summary>
    private readonly PostgreSqlFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlMartenTransactionalOutboxTests" /> class.
    /// </summary>
    /// <param name="fixture">The PostgreSQL fixture.</param>
    public PostgreSqlMartenTransactionalOutboxTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
        Skip.IfNot(_fixture.IsAvailable, PostgreSqlFixture.DockerRequiredMessage);
    }

    [SkippableFact]
    public async Task A_Marten_document_and_an_outbox_row_commit_together()
    {
        var outboxOptions = await CreateOutboxSchemaAsync().ConfigureAwait(true);
        var orderId = Guid.NewGuid();

        var documentStore = CreateDocumentStore();
        await using var configuredDocumentStore = documentStore.ConfigureAwait(false);
        var serviceProvider = BuildProvider(outboxOptions);
        await using var configuredServiceProvider = serviceProvider.ConfigureAwait(false);

        Guid messageId;

        var scope = serviceProvider.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var unitOfWork = (ScopedUnitOfWork) scope.ServiceProvider.GetRequiredService<IPostgreSqlTransactionProvider>();
            await unitOfWork.BeginAsync(_fixture.DataSource).ConfigureAwait(true);

            unitOfWork.TryGetCurrent(out _, out var transaction).Should().BeTrue();

            // SessionOptions.ForTransaction takes the application's open transaction, so the session writes through
            // the same connection LiteBus resolved from the ambient provider.
            var session = documentStore.LightweightSession(SessionOptions.ForTransaction(transaction!));

            await using (session.ConfigureAwait(false))
            {
                session.Store(new OrderDocument { Id = orderId, Total = 42m });

                var outbox = scope.ServiceProvider.GetRequiredService<ITransactionalOutbox>();
                var receipt = await outbox
                    .EnqueueAsync(new OrderSubmittedIntegrationEvent { OrderId = orderId })
                    .ConfigureAwait(true);

                messageId = receipt.Id;

                await session.SaveChangesAsync().ConfigureAwait(true);
            }

            await unitOfWork.CommitAsync().ConfigureAwait(true);
        }

        var querySession = documentStore.QuerySession();
        await using var configuredQuerySession = querySession.ConfigureAwait(false);
        var stored = await querySession.LoadAsync<OrderDocument>(orderId).ConfigureAwait(true);

        stored.Should().NotBeNull();
        (await CountOutboxRowsAsync(outboxOptions, messageId).ConfigureAwait(true)).Should().Be(1);
    }

    [SkippableFact]
    public async Task Neither_the_document_nor_the_outbox_row_survives_a_rollback()
    {
        var outboxOptions = await CreateOutboxSchemaAsync().ConfigureAwait(true);
        var orderId = Guid.NewGuid();

        var documentStore = CreateDocumentStore();
        await using var configuredDocumentStore = documentStore.ConfigureAwait(false);
        var serviceProvider = BuildProvider(outboxOptions);
        await using var configuredServiceProvider = serviceProvider.ConfigureAwait(false);

        Guid messageId;

        var scope = serviceProvider.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var unitOfWork = (ScopedUnitOfWork) scope.ServiceProvider.GetRequiredService<IPostgreSqlTransactionProvider>();
            await unitOfWork.BeginAsync(_fixture.DataSource).ConfigureAwait(true);

            unitOfWork.TryGetCurrent(out _, out var transaction).Should().BeTrue();

            // SessionOptions.ForTransaction takes the application's open transaction, so the session writes through
            // the same connection LiteBus resolved from the ambient provider.
            var session = documentStore.LightweightSession(SessionOptions.ForTransaction(transaction!));

            await using (session.ConfigureAwait(false))
            {
                session.Store(new OrderDocument { Id = orderId, Total = 42m });

                var outbox = scope.ServiceProvider.GetRequiredService<ITransactionalOutbox>();
                var receipt = await outbox
                    .EnqueueAsync(new OrderSubmittedIntegrationEvent { OrderId = orderId })
                    .ConfigureAwait(true);

                messageId = receipt.Id;

                await session.SaveChangesAsync().ConfigureAwait(true);
            }

            // The aggregate write and the outbox row share one transaction, so abandoning it must lose both. An
            // outbox row surviving here would publish an event for state that never existed.
            await unitOfWork.RollbackAsync().ConfigureAwait(true);
        }

        var querySession = documentStore.QuerySession();
        await using var configuredQuerySession = querySession.ConfigureAwait(false);
        var stored = await querySession.LoadAsync<OrderDocument>(orderId).ConfigureAwait(true);

        stored.Should().BeNull();
        (await CountOutboxRowsAsync(outboxOptions, messageId).ConfigureAwait(true)).Should().Be(0);
    }

    [SkippableFact]
    public async Task The_writer_refuses_to_enqueue_with_no_active_transaction()
    {
        var outboxOptions = await CreateOutboxSchemaAsync().ConfigureAwait(true);

        var serviceProvider = BuildProvider(outboxOptions);
        await using var configuredServiceProvider = serviceProvider.ConfigureAwait(false);

        var scope = serviceProvider.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            // The writer binds its store when it is resolved, so a handler that forgot to open the unit of work
            // fails at injection rather than committing the outbox row on its own. RequireActiveTransaction is the
            // default precisely so this cannot silently fall back to an independent commit.
            var act = () => scope.ServiceProvider.GetRequiredService<ITransactionalOutbox>();

            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*requires an active PostgreSQL transaction*");
        }
    }

    private DocumentStore CreateDocumentStore()
    {
        return DocumentStore.For(options =>
        {
            options.Connection(_fixture.ConnectionString);
            options.DatabaseSchemaName = "marten_outbox_tests";
            options.Schema.For<OrderDocument>().Identity(document => document.Id);
        });
    }

    private ServiceProvider BuildProvider(PostgreSqlOutboxStoreOptions outboxOptions)
    {
        var services = new ServiceCollection();
        services.AddSingleton(_fixture.DataSource);
        services.AddScoped<IPostgreSqlTransactionProvider, ScopedUnitOfWork>();

        services.AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddOutbox(outbox =>
            {
                outbox.Contracts.Register<OrderSubmittedIntegrationEvent>("orders.events.submitted");

                outbox.UsePostgreSqlStorage(postgres =>
                {
                    postgres.UseDataSource(_fixture.DataSource);
                    postgres.UseOptions(outboxOptions);
                    postgres.DisableSchemaInitialization();
                    postgres.EnableAmbientTransactionProvider();
                });
            });
        });

        return services.BuildServiceProvider();
    }

    private async Task<PostgreSqlOutboxStoreOptions> CreateOutboxSchemaAsync()
    {
        var outboxOptions = PostgreSqlTestInfrastructure.CreateOutboxStoreOptions(
            $"outbox_marten_{Guid.NewGuid():N}");

        await PostgreSqlTestInfrastructure
            .EnsureOutboxSchemaAsync(_fixture.DataSource, outboxOptions)
            .ConfigureAwait(false);

        return outboxOptions;
    }

    private async Task<int> CountOutboxRowsAsync(PostgreSqlOutboxStoreOptions options, Guid messageId)
    {
        var connection = await _fixture.DataSource.OpenConnectionAsync().ConfigureAwait(false);

        await using (connection.ConfigureAwait(false))
        {
            var command = connection.CreateCommand();

            await using (command.ConfigureAwait(false))
            {
                command.CommandText =
                    $"SELECT COUNT(*)::int FROM \"{options.SchemaName}\".\"{options.TableName}\" WHERE message_id = @message_id;";
                command.Parameters.AddWithValue("message_id", messageId);

                var count = await command.ExecuteScalarAsync().ConfigureAwait(false);
                return (int) count!;
            }
        }
    }

    /// <summary>
    ///     A Marten document written alongside the outbox row.
    /// </summary>
    public sealed class OrderDocument
    {
        /// <summary>
        ///     Gets or sets the document identity.
        /// </summary>
        public Guid Id { get; set; }

        /// <summary>
        ///     Gets or sets the order total.
        /// </summary>
        public decimal Total { get; set; }
    }

    /// <summary>
    ///     The integration event enqueued in the same transaction as the document.
    /// </summary>
    public sealed record OrderSubmittedIntegrationEvent
    {
        /// <summary>
        ///     Gets the submitted order identifier.
        /// </summary>
        public required Guid OrderId { get; init; }
    }

    /// <summary>
    ///     The application-owned unit of work both Marten and the outbox enlist on.
    /// </summary>
    /// <remarks>
    ///     This is the shape the transactional-writes guide documents: a scoped service that opens the transaction and
    ///     exposes the pair, so LiteBus resolves a bound store and Marten builds a session on the same connection.
    /// </remarks>
    private sealed class ScopedUnitOfWork : IPostgreSqlTransactionProvider, IAsyncDisposable
    {
        /// <summary>
        ///     The connection opened for the scope, owned by this unit of work.
        /// </summary>
        private NpgsqlConnection? _connection;

        /// <summary>
        ///     The transaction every write in the scope participates in.
        /// </summary>
        private NpgsqlTransaction? _transaction;

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            if (_transaction is not null)
            {
                await _transaction.DisposeAsync().ConfigureAwait(false);
            }

            if (_connection is not null)
            {
                await _connection.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <inheritdoc />
        public bool TryGetCurrent(
            [NotNullWhen(true)] out NpgsqlConnection? connection,
            [NotNullWhen(true)] out NpgsqlTransaction? transaction)
        {
            connection = _connection;
            transaction = _transaction;
            return _connection is not null && _transaction is not null;
        }

        /// <summary>
        ///     Opens the connection and transaction for the scope.
        /// </summary>
        /// <param name="dataSource">The data source shared by the application and LiteBus.</param>
        /// <returns>A task that completes once the transaction is open.</returns>
        public async Task BeginAsync(NpgsqlDataSource dataSource)
        {
            _connection = await dataSource.OpenConnectionAsync().ConfigureAwait(false);
            _transaction = await _connection.BeginTransactionAsync().ConfigureAwait(false);
        }

        /// <summary>
        ///     Commits every write made in the scope.
        /// </summary>
        /// <returns>A task that completes once the transaction is committed.</returns>
        public Task CommitAsync()
        {
            return _transaction!.CommitAsync();
        }

        /// <summary>
        ///     Abandons every write made in the scope.
        /// </summary>
        /// <returns>A task that completes once the transaction is rolled back.</returns>
        public Task RollbackAsync()
        {
            return _transaction!.RollbackAsync();
        }
    }
}
