using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.PostgreSql;
using LiteBus.Runtime.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace LiteBus.Extensions.IntegrationTests.AspNetCore;

/// <summary>
///     HTTP acceptance tests proving that outbox immediate dispatch serves a read-model projection before the
///     command POST responds, with the outbox processor as the retry fallback.
/// </summary>
public sealed class ImmediateDispatchHttpAcceptanceTests : IClassFixture<PostgreSqlFixture>
{
    /// <summary>
    ///     The contract name under which the placed-order event is registered.
    /// </summary>
    private const string OrderPlacedContractName = "orders.events.placed";

    /// <summary>
    ///     The projection status the dispatcher writes when an event is handled.
    /// </summary>
    private const string ProjectedStatus = "projected";

    /// <summary>
    ///     The JSON options shared by the test endpoints.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    ///     The shared PostgreSQL fixture.
    /// </summary>
    private readonly PostgreSqlFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ImmediateDispatchHttpAcceptanceTests" /> class.
    /// </summary>
    /// <param name="fixture">The PostgreSQL fixture.</param>
    public ImmediateDispatchHttpAcceptanceTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
        Skip.IfNot(_fixture.IsAvailable, PostgreSqlFixture.DockerRequiredMessage);
    }

    /// <summary>
    ///     Verifies that a GET issued right after the POST returns the projection the immediate dispatch wrote.
    /// </summary>
    /// <returns>A task that completes when the assertions finish.</returns>
    [SkippableFact]
    public async Task Post_WhenImmediateDispatchSucceeds_GetReturnsProjectionImmediately()
    {
        var outboxOptions = PostgreSqlTestInfrastructure.CreateOutboxStoreOptions();
        await PostgreSqlTestInfrastructure.EnsureOutboxSchemaAsync(_fixture.DataSource, outboxOptions).ConfigureAwait(false);
        var tables = await EnsureDomainTablesAsync().ConfigureAwait(false);
        var settings = new ProjectionDispatchSettings(_fixture.DataSource, tables.SchemaName, tables.ProjectionTableName, 0);

        using var host = await CreateHostAsync(outboxOptions, tables, settings).ConfigureAwait(false);
        using var client = host.GetTestClient();

        var orderId = Guid.NewGuid();
        var postResponse = await client.PostAsJsonAsync(
            "/orders",
            new PlaceOrderRequest { OrderId = orderId, Amount = 42m }).ConfigureAwait(false);
        postResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var receipt = await postResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
        receipt.GetProperty("receipt").GetString().Should().Contain(orderId.ToString());

        var getResponse = await client.GetAsync($"/orders/{orderId}/projection").ConfigureAwait(false);
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var projection = await getResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
        projection.GetProperty("orderId").GetGuid().Should().Be(orderId);
        projection.GetProperty("status").GetString().Should().Be(ProjectedStatus);

        settings.DispatchAttempts.Should().Be(1);

        var row = await ReadOutboxRowAsync(outboxOptions).ConfigureAwait(false);
        row.Should().NotBeNull();
        row!.Status.Should().Be(OutboxStatus.Published);
        row.AttemptCount.Should().Be(1);
    }

    /// <summary>
    ///     Verifies that a failing immediate dispatch still answers the POST with success and that the GET serves
    ///     the projection once the outbox processor has retried the envelope.
    /// </summary>
    /// <returns>A task that completes when the assertions finish.</returns>
    [SkippableFact]
    public async Task Post_WhenImmediateDispatchFails_GetReturnsProjectionAfterProcessorRetry()
    {
        var outboxOptions = PostgreSqlTestInfrastructure.CreateOutboxStoreOptions();
        await PostgreSqlTestInfrastructure.EnsureOutboxSchemaAsync(_fixture.DataSource, outboxOptions).ConfigureAwait(false);
        var tables = await EnsureDomainTablesAsync().ConfigureAwait(false);
        var settings = new ProjectionDispatchSettings(_fixture.DataSource, tables.SchemaName, tables.ProjectionTableName, 1);

        using var host = await CreateHostAsync(outboxOptions, tables, settings).ConfigureAwait(false);
        using var client = host.GetTestClient();

        var orderId = Guid.NewGuid();
        var postResponse = await client.PostAsJsonAsync(
            "/orders",
            new PlaceOrderRequest { OrderId = orderId, Amount = 15m }).ConfigureAwait(false);
        postResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var missing = await client.GetAsync($"/orders/{orderId}/projection").ConfigureAwait(false);
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var projectedOrderId = await settings.ProjectionWritten.WaitAsync(timeout.Token).ConfigureAwait(false);
        projectedOrderId.Should().Be(orderId);

        var getResponse = await client.GetAsync($"/orders/{orderId}/projection").ConfigureAwait(false);
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var projection = await getResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
        projection.GetProperty("orderId").GetGuid().Should().Be(orderId);
        projection.GetProperty("status").GetString().Should().Be(ProjectedStatus);

        settings.DispatchAttempts.Should().Be(2);
    }

    /// <summary>
    ///     Builds and starts a test host with PostgreSQL outbox storage, immediate dispatch, and order endpoints.
    /// </summary>
    /// <param name="outboxOptions">The PostgreSQL outbox store options.</param>
    /// <param name="tables">The isolated domain table names.</param>
    /// <param name="settings">The projection dispatcher configuration shared with the test.</param>
    /// <returns>The started host.</returns>
    private Task<IHost> CreateHostAsync(
        PostgreSqlOutboxStoreOptions outboxOptions,
        AcceptanceTables tables,
        ProjectionDispatchSettings settings)
    {
        ArgumentNullException.ThrowIfNull(outboxOptions);
        ArgumentNullException.ThrowIfNull(tables);
        ArgumentNullException.ThrowIfNull(settings);

        var builder = Host.CreateDefaultBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();

                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddSingleton(_fixture.DataSource);
                    services.AddSingleton(tables);

                    services.AddLiteBus(registry =>
                    {
                        registry.AddMessaging(_ =>
                        {
                        });

                        registry.AddCommands(commands =>
                        {
                            commands.Register<PlaceOrderCommand>();
                            commands.Register<PlaceOrderCommandHandler>();
                            commands.UseUnitOfWork<AcceptanceUnitOfWork>();
                        });

                        registry.AddOutbox(outbox =>
                        {
                            outbox.UsePostgreSqlStorage(postgres =>
                            {
                                postgres.UseDataSource(_fixture.DataSource);
                                postgres.UseOptions(outboxOptions);
                                postgres.DisableSchemaInitialization();
                            });

                            outbox.Contracts.Register<OrderPlacedEvent>(OrderPlacedContractName);

                            outbox.UseProcessorOptions(new OutboxProcessorOptions
                            {
                                BatchSize = 10,
                                LeaseOwner = "immediate-dispatch-http-acceptance",
                                Retry = new RetryOptions
                                {
                                    MaxAttempts = 5,
                                    InitialDelay = TimeSpan.FromSeconds(1),
                                    UseJitter = false
                                }
                            });

                            outbox.RegisterDispatcher(new ProjectionOutboxDispatchModule(settings));
                            outbox.EnableImmediateDispatch();
                            outbox.EnableOutboxProcessor(hostOptions =>
                            {
                                hostOptions.PollInterval = TimeSpan.FromMilliseconds(100);
                            });
                        });
                    });
                });

                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapPost("/orders", PlaceOrderAsync);
                        endpoints.MapGet("/orders/{orderId:guid}/projection", ReadProjectionAsync);
                    });
                });
            });

        return builder.StartAsync();
    }

    /// <summary>
    ///     Sends the posted order command and writes the receipt as the POST response.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    /// <returns>A task that completes when the response is written.</returns>
    private static async Task PlaceOrderAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var request = await JsonSerializer.DeserializeAsync<PlaceOrderRequest>(
            context.Request.Body,
            JsonOptions,
            context.RequestAborted).ConfigureAwait(false);

        if (request is null || request.OrderId == Guid.Empty)
        {
            context.Response.StatusCode = (int) HttpStatusCode.BadRequest;
            return;
        }

        var mediator = context.RequestServices.GetRequiredService<ICommandMediator>();
        var receipt = await mediator.SendAsync(
            new PlaceOrderCommand { OrderId = request.OrderId, Amount = request.Amount },
            cancellationToken: context.RequestAborted).ConfigureAwait(false);

        context.Response.StatusCode = (int) HttpStatusCode.OK;
        context.Response.ContentType = "application/json";
        await JsonSerializer.SerializeAsync(
            context.Response.Body,
            new PlaceOrderResponse { OrderId = request.OrderId, Receipt = receipt },
            JsonOptions,
            context.RequestAborted).ConfigureAwait(false);
    }

    /// <summary>
    ///     Reads the projection row for one order and writes it as the GET response.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    /// <returns>A task that completes when the response is written.</returns>
    private static async Task ReadProjectionAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!Guid.TryParse(context.Request.RouteValues["orderId"]?.ToString(), out var orderId))
        {
            context.Response.StatusCode = (int) HttpStatusCode.NotFound;
            return;
        }

        var dataSource = context.RequestServices.GetRequiredService<NpgsqlDataSource>();
        var tables = context.RequestServices.GetRequiredService<AcceptanceTables>();

        var connection = await dataSource.OpenConnectionAsync(context.RequestAborted).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var command = connection.CreateCommand();
            await using (command.ConfigureAwait(false))
            {
                command.CommandText =
                    $"""
                     SELECT status FROM "{tables.SchemaName}"."{tables.ProjectionTableName}"
                     WHERE order_id = @order_id;
                     """;
                command.Parameters.AddWithValue("order_id", orderId);

                var status = await command.ExecuteScalarAsync(context.RequestAborted).ConfigureAwait(false) as string;

                if (status is null)
                {
                    context.Response.StatusCode = (int) HttpStatusCode.NotFound;
                    return;
                }

                context.Response.StatusCode = (int) HttpStatusCode.OK;
                context.Response.ContentType = "application/json";
                await JsonSerializer.SerializeAsync(
                    context.Response.Body,
                    new OrderProjectionResponse { OrderId = orderId, Status = status },
                    JsonOptions,
                    context.RequestAborted).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    ///     Creates isolated order and projection tables for one test run.
    /// </summary>
    /// <returns>The isolated domain table names.</returns>
    private async Task<AcceptanceTables> EnsureDomainTablesAsync()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var tables = new AcceptanceTables
        {
            SchemaName = PostgreSqlTestInfrastructure.TestSchemaName,
            OrdersTableName = $"orders_immhttp_{suffix}",
            ProjectionTableName = $"projection_immhttp_{suffix}"
        };

        var connection = await _fixture.DataSource.OpenConnectionAsync().ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var command = connection.CreateCommand();
            await using (command.ConfigureAwait(false))
            {
                command.CommandText =
                    $"""
                     CREATE SCHEMA IF NOT EXISTS "{tables.SchemaName}";
                     CREATE TABLE IF NOT EXISTS "{tables.SchemaName}"."{tables.OrdersTableName}" (
                         order_id uuid NOT NULL PRIMARY KEY,
                         amount numeric NOT NULL);
                     CREATE TABLE IF NOT EXISTS "{tables.SchemaName}"."{tables.ProjectionTableName}" (
                         order_id uuid NOT NULL PRIMARY KEY,
                         status text NOT NULL);
                     """;

                await command.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
        }

        return tables;
    }

    /// <summary>
    ///     Reads the single outbox row stored by one test run.
    /// </summary>
    /// <param name="options">The PostgreSQL outbox store options.</param>
    /// <returns>The stored row, or <see langword="null" /> when the table holds no row.</returns>
    private async Task<StoredOutboxRow?> ReadOutboxRowAsync(PostgreSqlOutboxStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var connection = await _fixture.DataSource.OpenConnectionAsync().ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var command = connection.CreateCommand();
            await using (command.ConfigureAwait(false))
            {
                command.CommandText =
                    $"""
                     SELECT status, attempt_count FROM "{options.SchemaName}"."{options.TableName}";
                     """;

                var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
                await using (reader.ConfigureAwait(false))
                {
                    if (!await reader.ReadAsync().ConfigureAwait(false))
                    {
                        return null;
                    }

                    return new StoredOutboxRow
                    {
                        Status = (OutboxStatus) reader.GetInt32(0),
                        AttemptCount = reader.GetInt32(1)
                    };
                }
            }
        }
    }

    /// <summary>
    ///     The isolated domain table names used by one acceptance test run.
    /// </summary>
    public sealed record AcceptanceTables
    {
        /// <summary>
        ///     Gets the PostgreSQL schema holding the test tables.
        /// </summary>
        public required string SchemaName { get; init; }

        /// <summary>
        ///     Gets the domain orders table name.
        /// </summary>
        public required string OrdersTableName { get; init; }

        /// <summary>
        ///     Gets the read-model projection table name.
        /// </summary>
        public required string ProjectionTableName { get; init; }
    }

    /// <summary>
    ///     A command placing one order through the POST endpoint.
    /// </summary>
    public sealed record PlaceOrderCommand : ICommand<string>
    {
        /// <summary>
        ///     Gets the order identifier carried by the command.
        /// </summary>
        public required Guid OrderId { get; init; }

        /// <summary>
        ///     Gets the order amount carried by the command.
        /// </summary>
        public decimal Amount { get; init; }
    }

    /// <summary>
    ///     Saves the order and stages its placed event through the unit-of-work transaction.
    /// </summary>
    public sealed class PlaceOrderCommandHandler : ICommandHandler<PlaceOrderCommand, string>
    {
        /// <summary>
        ///     The scoped unit of work owning the mediation transaction.
        /// </summary>
        private readonly AcceptanceUnitOfWork _unitOfWork;

        /// <summary>
        ///     The registration binding outbox writers to the mediation transaction.
        /// </summary>
        private readonly PostgreSqlOutboxStoreRegistration _registration;

        /// <summary>
        ///     The factory creating the staged envelope.
        /// </summary>
        private readonly IOutboxEnvelopeFactory _envelopeFactory;

        /// <summary>
        ///     The isolated domain table names.
        /// </summary>
        private readonly AcceptanceTables _tables;

        /// <summary>
        ///     Initializes a new instance of the <see cref="PlaceOrderCommandHandler" /> class.
        /// </summary>
        /// <param name="unitOfWork">The scoped unit of work owning the mediation transaction.</param>
        /// <param name="registration">The registration binding outbox writers to the mediation transaction.</param>
        /// <param name="envelopeFactory">The factory creating the staged envelope.</param>
        /// <param name="tables">The isolated domain table names.</param>
        public PlaceOrderCommandHandler(
            AcceptanceUnitOfWork unitOfWork,
            PostgreSqlOutboxStoreRegistration registration,
            IOutboxEnvelopeFactory envelopeFactory,
            AcceptanceTables tables)
        {
            ArgumentNullException.ThrowIfNull(unitOfWork);
            ArgumentNullException.ThrowIfNull(registration);
            ArgumentNullException.ThrowIfNull(envelopeFactory);
            ArgumentNullException.ThrowIfNull(tables);

            _unitOfWork = unitOfWork;
            _registration = registration;
            _envelopeFactory = envelopeFactory;
            _tables = tables;
        }

        /// <summary>
        ///     Handles the command.
        /// </summary>
        /// <param name="message">The command.</param>
        /// <param name="cancellationToken">A token that cancels the operation.</param>
        /// <returns>The order receipt.</returns>
        public async Task<string> HandleAsync(PlaceOrderCommand message, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(message);

            var insert = _unitOfWork.Connection.CreateCommand();
            await using (insert.ConfigureAwait(false))
            {
                insert.Transaction = _unitOfWork.Transaction;
                insert.CommandText =
                    $"""
                     INSERT INTO "{_tables.SchemaName}"."{_tables.OrdersTableName}" (order_id, amount)
                     VALUES (@order_id, @amount);
                     """;
                insert.Parameters.AddWithValue("order_id", message.OrderId);
                insert.Parameters.AddWithValue("amount", message.Amount);
                await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            var store = _registration.CreateTransactionalStore(_unitOfWork.Connection, _unitOfWork.Transaction);
            var writer = new StoreBoundTransactionalOutbox(store, _envelopeFactory);

            await writer.EnqueueAsync(
                OutboxEnqueueItem<OrderPlacedEvent>.From(new OrderPlacedEvent
                {
                    OrderId = message.OrderId,
                    Amount = message.Amount
                }),
                cancellationToken).ConfigureAwait(false);

            return $"order:{message.OrderId}";
        }
    }

    /// <summary>
    ///     An event raised when an order is placed, dispatched immediately after the unit of work commits.
    /// </summary>
    [DispatchImmediately]
    public sealed record OrderPlacedEvent
    {
        /// <summary>
        ///     Gets the placed order identifier.
        /// </summary>
        public required Guid OrderId { get; init; }

        /// <summary>
        ///     Gets the placed order amount.
        /// </summary>
        public decimal Amount { get; init; }
    }

    /// <summary>
    ///     A PostgreSQL unit of work opening one connection and transaction per command mediation.
    /// </summary>
    public sealed class AcceptanceUnitOfWork : IUnitOfWork, IUnitOfWorkBoundary, IAsyncDisposable
    {
        /// <summary>
        ///     The shared PostgreSQL data source.
        /// </summary>
        private readonly NpgsqlDataSource _dataSource;

        /// <summary>
        ///     The mediation connection opened by <see cref="BeginAsync" />, or <see langword="null" /> before it runs.
        /// </summary>
        private NpgsqlConnection? _connection;

        /// <summary>
        ///     The mediation transaction opened by <see cref="BeginAsync" />, or <see langword="null" /> before it runs.
        /// </summary>
        private NpgsqlTransaction? _transaction;

        /// <summary>
        ///     Initializes a new instance of the <see cref="AcceptanceUnitOfWork" /> class.
        /// </summary>
        /// <param name="dataSource">The shared PostgreSQL data source.</param>
        public AcceptanceUnitOfWork(NpgsqlDataSource dataSource)
        {
            ArgumentNullException.ThrowIfNull(dataSource);

            _dataSource = dataSource;
        }

        /// <summary>
        ///     Gets the mediation connection.
        /// </summary>
        /// <exception cref="InvalidOperationException"><see cref="BeginAsync" /> has not run yet.</exception>
        public NpgsqlConnection Connection =>
            _connection ?? throw new InvalidOperationException("The unit of work has no active connection because BeginAsync has not run.");

        /// <summary>
        ///     Gets the mediation transaction.
        /// </summary>
        /// <exception cref="InvalidOperationException"><see cref="BeginAsync" /> has not run yet.</exception>
        public NpgsqlTransaction Transaction =>
            _transaction ?? throw new InvalidOperationException("The unit of work has no active transaction because BeginAsync has not run.");

        /// <inheritdoc />
        public async Task BeginAsync(CancellationToken cancellationToken = default)
        {
            _connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            _transaction = await _connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task CommitAsync(CancellationToken cancellationToken = default)
        {
            if (_transaction is not null)
            {
                await _transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                await _transaction.DisposeAsync().ConfigureAwait(false);
                _transaction = null;
            }
        }

        /// <inheritdoc />
        public async Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            if (_transaction is not null)
            {
                await _transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                await _transaction.DisposeAsync().ConfigureAwait(false);
                _transaction = null;
            }
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            if (_transaction is not null)
            {
                await _transaction.DisposeAsync().ConfigureAwait(false);
                _transaction = null;
            }

            if (_connection is not null)
            {
                await _connection.DisposeAsync().ConfigureAwait(false);
                _connection = null;
            }
        }
    }

    /// <summary>
    ///     The projection dispatcher configuration and outcome signals for one acceptance test run.
    /// </summary>
    public sealed class ProjectionDispatchSettings
    {
        /// <summary>
        ///     The signal completed when a projection row is written.
        /// </summary>
        private readonly TaskCompletionSource<Guid> _projectionWritten = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        ///     The number of dispatch attempts recorded so far.
        /// </summary>
        private int _dispatchAttempts;

        /// <summary>
        ///     Initializes a new instance of the <see cref="ProjectionDispatchSettings" /> class.
        /// </summary>
        /// <param name="dataSource">The shared PostgreSQL data source.</param>
        /// <param name="schemaName">The PostgreSQL schema holding the projection table.</param>
        /// <param name="projectionTableName">The read-model projection table name.</param>
        /// <param name="failFirstAttempts">The number of leading dispatch attempts that throw.</param>
        public ProjectionDispatchSettings(
            NpgsqlDataSource dataSource,
            string schemaName,
            string projectionTableName,
            int failFirstAttempts)
        {
            ArgumentNullException.ThrowIfNull(dataSource);
            ArgumentException.ThrowIfNullOrWhiteSpace(schemaName);
            ArgumentException.ThrowIfNullOrWhiteSpace(projectionTableName);
            ArgumentOutOfRangeException.ThrowIfNegative(failFirstAttempts);

            DataSource = dataSource;
            SchemaName = schemaName;
            ProjectionTableName = projectionTableName;
            FailFirstAttempts = failFirstAttempts;
        }

        /// <summary>
        ///     Gets the shared PostgreSQL data source.
        /// </summary>
        public NpgsqlDataSource DataSource { get; }

        /// <summary>
        ///     Gets the PostgreSQL schema holding the projection table.
        /// </summary>
        public string SchemaName { get; }

        /// <summary>
        ///     Gets the read-model projection table name.
        /// </summary>
        public string ProjectionTableName { get; }

        /// <summary>
        ///     Gets the number of leading dispatch attempts that throw.
        /// </summary>
        public int FailFirstAttempts { get; }

        /// <summary>
        ///     Gets the number of dispatch attempts recorded so far.
        /// </summary>
        public int DispatchAttempts => _dispatchAttempts;

        /// <summary>
        ///     Gets the task completed with the order identifier when a projection row is written.
        /// </summary>
        public Task<Guid> ProjectionWritten => _projectionWritten.Task;

        /// <summary>
        ///     Records one dispatch attempt.
        /// </summary>
        /// <returns>The one-based attempt number.</returns>
        public int RecordAttempt()
        {
            return Interlocked.Increment(ref _dispatchAttempts);
        }

        /// <summary>
        ///     Signals that the projection row for one order was written.
        /// </summary>
        /// <param name="orderId">The projected order identifier.</param>
        public void ReportProjectionWritten(Guid orderId)
        {
            _projectionWritten.TrySetResult(orderId);
        }
    }

    /// <summary>
    ///     An outbox dispatcher writing the placed-order projection row the GET endpoint reads.
    /// </summary>
    public sealed class ProjectionOutboxDispatcher : IOutboxDispatcher
    {
        /// <summary>
        ///     The registry resolving persisted contracts back to event types.
        /// </summary>
        private readonly IContractReader _contracts;

        /// <summary>
        ///     The serializer hydrating the persisted payload.
        /// </summary>
        private readonly IMessageSerializer _serializer;

        /// <summary>
        ///     The projection dispatcher configuration and outcome signals.
        /// </summary>
        private readonly ProjectionDispatchSettings _settings;

        /// <summary>
        ///     Initializes a new instance of the <see cref="ProjectionOutboxDispatcher" /> class.
        /// </summary>
        /// <param name="contracts">The registry resolving persisted contracts back to event types.</param>
        /// <param name="serializer">The serializer hydrating the persisted payload.</param>
        /// <param name="settings">The projection dispatcher configuration and outcome signals.</param>
        public ProjectionOutboxDispatcher(
            IContractReader contracts,
            IMessageSerializer serializer,
            ProjectionDispatchSettings settings)
        {
            ArgumentNullException.ThrowIfNull(contracts);
            ArgumentNullException.ThrowIfNull(serializer);
            ArgumentNullException.ThrowIfNull(settings);

            _contracts = contracts;
            _serializer = serializer;
            _settings = settings;
        }

        /// <inheritdoc />
        public async Task DispatchAsync(OutboxEnvelope message, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(message);

            var attempt = _settings.RecordAttempt();

            if (attempt <= _settings.FailFirstAttempts)
            {
                throw new InvalidOperationException($"The projection dispatcher fails attempt {attempt} for envelope {message.Id}.");
            }

            var messageType = _contracts.GetMessageType(message.ContractName, message.ContractVersion);
            var placed = (OrderPlacedEvent) await _serializer.DeserializeAsync(messageType, message.Payload, cancellationToken).ConfigureAwait(false);

            var connection = await _settings.DataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using (connection.ConfigureAwait(false))
            {
                var command = connection.CreateCommand();
                await using (command.ConfigureAwait(false))
                {
                    command.CommandText =
                        $"""
                         INSERT INTO "{_settings.SchemaName}"."{_settings.ProjectionTableName}" (order_id, status)
                         VALUES (@order_id, @status)
                         ON CONFLICT (order_id) DO UPDATE SET status = EXCLUDED.status;
                         """;
                    command.Parameters.AddWithValue("order_id", placed.OrderId);
                    command.Parameters.AddWithValue("status", ProjectedStatus);
                    await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }
            }

            _settings.ReportProjectionWritten(placed.OrderId);
        }
    }

    /// <summary>
    ///     An outbox dispatcher module registering the projection dispatcher.
    /// </summary>
    private sealed class ProjectionOutboxDispatchModule : IOutboxDispatcherModule
    {
        /// <summary>
        ///     The projection dispatcher configuration shared with the test.
        /// </summary>
        private readonly ProjectionDispatchSettings _settings;

        /// <summary>
        ///     Initializes a new instance of the <see cref="ProjectionOutboxDispatchModule" /> class.
        /// </summary>
        /// <param name="settings">The projection dispatcher configuration shared with the test.</param>
        public ProjectionOutboxDispatchModule(ProjectionDispatchSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);

            _settings = settings;
        }

        /// <inheritdoc />
        public void Build(IModuleConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            configuration.DependencyRegistry.Register(new DependencyDescriptor(typeof(ProjectionDispatchSettings), _settings));
            configuration.DependencyRegistry.Register(new DependencyDescriptor(typeof(IOutboxDispatcher), typeof(ProjectionOutboxDispatcher)));
        }
    }

    /// <summary>
    ///     The JSON body posted to the order endpoint.
    /// </summary>
    private sealed record PlaceOrderRequest
    {
        /// <summary>
        ///     Gets the order identifier.
        /// </summary>
        public required Guid OrderId { get; init; }

        /// <summary>
        ///     Gets the order amount.
        /// </summary>
        public decimal Amount { get; init; }
    }

    /// <summary>
    ///     The JSON body returned by the order endpoint.
    /// </summary>
    private sealed record PlaceOrderResponse
    {
        /// <summary>
        ///     Gets the placed order identifier.
        /// </summary>
        public required Guid OrderId { get; init; }

        /// <summary>
        ///     Gets the command receipt.
        /// </summary>
        public required string Receipt { get; init; }
    }

    /// <summary>
    ///     The JSON body returned by the projection endpoint.
    /// </summary>
    private sealed record OrderProjectionResponse
    {
        /// <summary>
        ///     Gets the projected order identifier.
        /// </summary>
        public required Guid OrderId { get; init; }

        /// <summary>
        ///     Gets the projection status.
        /// </summary>
        public required string Status { get; init; }
    }

    /// <summary>
    ///     The stored outbox row state asserted after immediate dispatch.
    /// </summary>
    private sealed record StoredOutboxRow
    {
        /// <summary>
        ///     Gets the publication status.
        /// </summary>
        public required OutboxStatus Status { get; init; }

        /// <summary>
        ///     Gets the number of publication attempts.
        /// </summary>
        public required int AttemptCount { get; init; }
    }
}
