using LiteBus.Messaging.Abstractions.DurableMessaging;
using LiteBus.Outbox;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.Marten;
using LiteBus.Outbox.Storage.PostgreSql;
using LiteBus.Outbox.Storage.PostgreSql.Exceptions;
using Marten;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Storage.IntegrationTests.PostgreSql;

/// <summary>
///     Verifies that the Marten queued transactional outbox commits documents and outbox rows in one session save.
/// </summary>
/// <remarks>
///     <para>
///         The writer queues the schema-owned outbox insert on the scoped <see cref="IDocumentSession" /> through
///         <c>QueueSqlCommand</c>, so one session serves reads and writes without an application-owned transaction.
///     </para>
/// </remarks>
public sealed class MartenQueuedTransactionalOutboxTests : IClassFixture<PostgreSqlFixture>
{
    /// <summary>
    ///     The PostgreSQL fixture shared across tests.
    /// </summary>
    private readonly PostgreSqlFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="MartenQueuedTransactionalOutboxTests" /> class.
    /// </summary>
    /// <param name="fixture">The PostgreSQL fixture.</param>
    public MartenQueuedTransactionalOutboxTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
        Skip.IfNot(_fixture.IsAvailable, PostgreSqlFixture.DockerRequiredMessage);
    }

    [SkippableFact]
    public async Task A_Marten_document_and_an_outbox_row_commit_together()
    {
        var outboxOptions = await CreateOutboxSchemaAsync().ConfigureAwait(true);
        var orderId = Guid.NewGuid();

        var serviceProvider = BuildProvider(outboxOptions);
        await using var configuredServiceProvider = serviceProvider.ConfigureAwait(false);

        Guid messageId;

        var scope = serviceProvider.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var session = scope.ServiceProvider.GetRequiredService<IDocumentSession>();
            session.Store(new OrderDocument { Id = orderId, Total = 42m });

            var outbox = scope.ServiceProvider.GetRequiredService<ITransactionalOutbox>();
            var receipt = await outbox
                .EnqueueAsync(new OrderSubmittedIntegrationEvent { OrderId = orderId })
                .ConfigureAwait(true);

            messageId = receipt.Id;
            receipt.Outcome.Should().Be(OutboxEnqueueOutcome.Enqueued);

            await session.SaveChangesAsync().ConfigureAwait(true);
        }

        var documentStore = serviceProvider.GetRequiredService<IDocumentStore>();
        var querySession = documentStore.QuerySession();
        await using var configuredQuerySession = querySession.ConfigureAwait(false);
        var stored = await querySession.LoadAsync<OrderDocument>(orderId).ConfigureAwait(true);

        stored.Should().NotBeNull();
        (await CountOutboxRowsAsync(outboxOptions, messageId).ConfigureAwait(true)).Should().Be(1);
    }

    [SkippableFact]
    public async Task Neither_the_document_nor_the_outbox_row_is_stored_without_save_changes()
    {
        var outboxOptions = await CreateOutboxSchemaAsync().ConfigureAwait(true);
        var orderId = Guid.NewGuid();

        var serviceProvider = BuildProvider(outboxOptions);
        await using var configuredServiceProvider = serviceProvider.ConfigureAwait(false);

        Guid messageId;

        var scope = serviceProvider.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var session = scope.ServiceProvider.GetRequiredService<IDocumentSession>();
            session.Store(new OrderDocument { Id = orderId, Total = 42m });

            var outbox = scope.ServiceProvider.GetRequiredService<ITransactionalOutbox>();
            var receipt = await outbox
                .EnqueueAsync(new OrderSubmittedIntegrationEvent { OrderId = orderId })
                .ConfigureAwait(true);

            messageId = receipt.Id;

            // The scope ends without SaveChangesAsync, so the queued insert must be abandoned with the document.
        }

        var documentStore = serviceProvider.GetRequiredService<IDocumentStore>();
        var querySession = documentStore.QuerySession();
        await using var configuredQuerySession = querySession.ConfigureAwait(false);
        var stored = await querySession.LoadAsync<OrderDocument>(orderId).ConfigureAwait(true);

        stored.Should().BeNull();
        (await CountOutboxRowsAsync(outboxOptions, messageId).ConfigureAwait(true)).Should().Be(0);
    }

    [SkippableFact]
    public async Task Staging_the_same_message_twice_in_one_session_stores_one_row()
    {
        var outboxOptions = await CreateOutboxSchemaAsync().ConfigureAwait(true);
        var orderId = Guid.NewGuid();
        var messageId = Guid.NewGuid();

        var serviceProvider = BuildProvider(outboxOptions);
        await using var configuredServiceProvider = serviceProvider.ConfigureAwait(false);

        var scope = serviceProvider.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var session = scope.ServiceProvider.GetRequiredService<IDocumentSession>();
            session.Store(new OrderDocument { Id = orderId, Total = 42m });

            var outbox = scope.ServiceProvider.GetRequiredService<ITransactionalOutbox>();
            var item = OutboxEnqueueItem<OrderSubmittedIntegrationEvent>.WithIdentity(
                new OrderSubmittedIntegrationEvent { OrderId = orderId },
                messageId);

            var first = await outbox.EnqueueAsync(item).ConfigureAwait(true);
            var second = await outbox.EnqueueAsync(item).ConfigureAwait(true);

            first.Outcome.Should().Be(OutboxEnqueueOutcome.Enqueued);
            second.Outcome.Should().Be(OutboxEnqueueOutcome.AlreadyEnqueued);
            second.Id.Should().Be(messageId);

            await session.SaveChangesAsync().ConfigureAwait(true);
        }

        (await CountOutboxRowsAsync(outboxOptions, messageId).ConfigureAwait(true)).Should().Be(1);
    }

    [SkippableFact]
    public async Task A_message_whose_id_is_already_stored_returns_already_enqueued()
    {
        var outboxOptions = await CreateOutboxSchemaAsync().ConfigureAwait(true);
        var orderId = Guid.NewGuid();
        var messageId = Guid.NewGuid();

        var serviceProvider = BuildProvider(outboxOptions);
        await using var configuredServiceProvider = serviceProvider.ConfigureAwait(false);

        var firstScope = serviceProvider.CreateAsyncScope();
        await using (firstScope.ConfigureAwait(false))
        {
            var session = firstScope.ServiceProvider.GetRequiredService<IDocumentSession>();
            session.Store(new OrderDocument { Id = orderId, Total = 42m });

            var outbox = firstScope.ServiceProvider.GetRequiredService<ITransactionalOutbox>();
            var item = OutboxEnqueueItem<OrderSubmittedIntegrationEvent>.WithIdentity(
                new OrderSubmittedIntegrationEvent { OrderId = orderId },
                messageId);

            await outbox.EnqueueAsync(item).ConfigureAwait(true);
            await session.SaveChangesAsync().ConfigureAwait(true);
        }

        var secondScope = serviceProvider.CreateAsyncScope();
        await using (secondScope.ConfigureAwait(false))
        {
            var outbox = secondScope.ServiceProvider.GetRequiredService<ITransactionalOutbox>();
            var item = OutboxEnqueueItem<OrderSubmittedIntegrationEvent>.WithIdentity(
                new OrderSubmittedIntegrationEvent { OrderId = orderId },
                messageId);

            var receipt = await outbox.EnqueueAsync(item).ConfigureAwait(true);

            receipt.Outcome.Should().Be(OutboxEnqueueOutcome.AlreadyEnqueued);
            receipt.Id.Should().Be(messageId);
        }

        (await CountOutboxRowsAsync(outboxOptions, messageId).ConfigureAwait(true)).Should().Be(1);
    }

    [SkippableFact]
    public async Task A_strict_submission_with_a_conflicting_key_throws()
    {
        var outboxOptions = await CreateOutboxSchemaAsync().ConfigureAwait(true);

        var serviceProvider = BuildProvider(outboxOptions);
        await using var configuredServiceProvider = serviceProvider.ConfigureAwait(false);

        var firstScope = serviceProvider.CreateAsyncScope();
        await using (firstScope.ConfigureAwait(false))
        {
            var session = firstScope.ServiceProvider.GetRequiredService<IDocumentSession>();
            session.Store(new OrderDocument { Id = Guid.NewGuid(), Total = 42m });

            var outbox = firstScope.ServiceProvider.GetRequiredService<ITransactionalOutbox>();
            var item = OutboxEnqueueItem<OrderSubmittedIntegrationEvent>.WithIdempotency(
                new OrderSubmittedIntegrationEvent { OrderId = Guid.NewGuid() },
                "order-submit");

            await outbox.EnqueueAsync(item).ConfigureAwait(true);
            await session.SaveChangesAsync().ConfigureAwait(true);
        }

        var secondScope = serviceProvider.CreateAsyncScope();
        await using (secondScope.ConfigureAwait(false))
        {
            var outbox = secondScope.ServiceProvider.GetRequiredService<ITransactionalOutbox>();
            var metadata = OutboxEnqueueMetadata.Immediate with
            {
                Idempotency = new Idempotency.Keyed("order-submit", IdempotencyConflictMode.Strict)
            };
            var item = OutboxEnqueueItem<OrderSubmittedIntegrationEvent>.From(
                new OrderSubmittedIntegrationEvent { OrderId = Guid.NewGuid() },
                metadata);

            var act = () => outbox.EnqueueAsync(item);

            await act.Should().ThrowAsync<IdempotencyConflictException>().ConfigureAwait(true);
        }
    }

    [SkippableFact]
    public void Composing_both_the_ambient_provider_and_the_Marten_writer_throws()
    {
        var outboxOptions = PostgreSqlTestInfrastructure.CreateOutboxStoreOptions();
        var services = new ServiceCollection();
        services.AddSingleton(_fixture.DataSource);

        var act = () => services.AddLiteBus(registry =>
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
                    postgres.UseMartenTransactionalOutbox();
                });
            });
        });

        act.Should().Throw<OutboxPostgreSqlStorageConfigurationException>()
            .WithMessage("*EnableAmbientTransactionProvider*UseMartenTransactionalOutbox*");
    }

    private ServiceProvider BuildProvider(PostgreSqlOutboxStoreOptions outboxOptions)
    {
        var services = new ServiceCollection();
        services.AddSingleton(_fixture.DataSource);
        services.AddMarten(options =>
        {
            options.Connection(_fixture.ConnectionString);
            options.DatabaseSchemaName = "marten_queued_outbox_tests";
            options.Schema.For<OrderDocument>().Identity(document => document.Id);
        });

        services.AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddOutbox(outbox =>
            {
                outbox.Contracts.Register<OrderSubmittedIntegrationEvent>("orders.events.submitted");

                outbox.UsePostgreSqlStorage(pg =>
                {
                    pg.UseDataSource(_fixture.DataSource)
                        .UseOptions(outboxOptions)
                        .DisableSchemaInitialization()
                        .UseMartenTransactionalOutbox();
                });
            });
        });

        return services.BuildServiceProvider();
    }

    private async Task<PostgreSqlOutboxStoreOptions> CreateOutboxSchemaAsync()
    {
        var outboxOptions = PostgreSqlTestInfrastructure.CreateOutboxStoreOptions(
            $"outbox_marten_queued_{Guid.NewGuid():N}");

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
    ///     The integration event enqueued in the same session as the document.
    /// </summary>
    public sealed record OrderSubmittedIntegrationEvent
    {
        /// <summary>
        ///     Gets the submitted order identifier.
        /// </summary>
        public required Guid OrderId { get; init; }
    }
}
