using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Commands.Storage.Marten;
using LiteBus.Messaging.Abstractions;
using LiteBus.Storage.IntegrationTests.PostgreSql;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LiteBus.Storage.IntegrationTests.PostgreSql.Marten;

/// <summary>
///     Integration tests for the idempotency settlement order: the Marten idempotency store settles after the Marten unit of work commits, and releases the key when the commit fails.
/// </summary>
public sealed class MartenIdempotencySettlementOrderTests : IClassFixture<PostgreSqlFixture>
{
    /// <summary>
    ///     The PostgreSQL state a unique-constraint violation reports.
    /// </summary>
    private const string UniqueViolationSqlState = "23505";

    /// <summary>
    ///     The shared PostgreSQL container fixture.
    /// </summary>
    private readonly PostgreSqlFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="MartenIdempotencySettlementOrderTests" /> class.
    /// </summary>
    /// <param name="fixture">The shared PostgreSQL container fixture.</param>
    public MartenIdempotencySettlementOrderTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
        Skip.IfNot(_fixture.IsAvailable, PostgreSqlFixture.DockerRequiredMessage);
    }

    [SkippableFact]
    public async Task SendAsync_ShouldCommitTheOrderAndCompleteTheKeyAfterTheCommit()
    {
        var counter = new SettlementHandlerCounter();
        var provider = BuildProvider(CreateSchemaName(), counter);
        await using var configuredProvider = provider.ConfigureAwait(false);
        var mediator = provider.GetRequiredService<ICommandMediator>();
        var documentStore = provider.GetRequiredService<IDocumentStore>();

        var command = new ApplySettlementOrderCommand(Guid.NewGuid(), $"order-{Guid.NewGuid():N}", FailHandler: false);
        var receipt = await mediator.SendAsync(command).ConfigureAwait(false);
        var expectedReceipt = $"receipt:{command.OrderId}:1";

        receipt.Should().Be(expectedReceipt);

        var stored = await LoadDocumentAsync(documentStore, command.OrderId).ConfigureAwait(false);
        stored.Should().NotBeNull();
        stored!.Receipt.Should().Be(expectedReceipt);

        var keys = await ReadStoredKeysAsync(documentStore).ConfigureAwait(false);
        keys.Should().ContainSingle();
        keys[0].IsCompleted.Should().BeTrue();
        keys[0].Payload.Should().Contain(expectedReceipt);

        var repeat = await mediator.TrySendAsync(command).ConfigureAwait(false);
        repeat.Outcome.Should().Be(MediationOutcome.Answered);
        repeat.Value.Should().Be(expectedReceipt);
        counter.Runs.Should().Be(1);
    }

    [SkippableFact]
    public async Task SendAsync_WhenTheCommitFails_ShouldReleaseTheKeyAndThrowTheCommitException()
    {
        var counter = new SettlementHandlerCounter();
        var provider = BuildProvider(CreateSchemaName(), counter);
        await using var configuredProvider = provider.ConfigureAwait(false);
        var mediator = provider.GetRequiredService<ICommandMediator>();
        var documentStore = provider.GetRequiredService<IDocumentStore>();

        var reference = $"conflict-{Guid.NewGuid():N}";
        await SeedDocumentAsync(documentStore, Guid.NewGuid(), reference, "seed").ConfigureAwait(false);

        var command = new ApplySettlementOrderCommand(Guid.NewGuid(), reference, FailHandler: false);
        var act = async () => await mediator.SendAsync(command).ConfigureAwait(false);

        await act.Should().ThrowAsync<Exception>()
            .Where(exception => ContainsUniqueViolation(exception)).ConfigureAwait(false);
        counter.Runs.Should().Be(1);

        var stored = await LoadDocumentAsync(documentStore, command.OrderId).ConfigureAwait(false);
        stored.Should().BeNull();

        var keys = await ReadStoredKeysAsync(documentStore).ConfigureAwait(false);
        keys.Should().BeEmpty();
    }

    [SkippableFact]
    public async Task Retry_AfterAFailedCommit_ShouldRunTheHandlerAgainInsteadOfAnswering()
    {
        var counter = new SettlementHandlerCounter();
        var provider = BuildProvider(CreateSchemaName(), counter);
        await using var configuredProvider = provider.ConfigureAwait(false);
        var mediator = provider.GetRequiredService<ICommandMediator>();
        var documentStore = provider.GetRequiredService<IDocumentStore>();

        var reference = $"conflict-{Guid.NewGuid():N}";
        var seedId = Guid.NewGuid();
        await SeedDocumentAsync(documentStore, seedId, reference, "seed").ConfigureAwait(false);

        var command = new ApplySettlementOrderCommand(Guid.NewGuid(), reference, FailHandler: false);
        var act = async () => await mediator.SendAsync(command).ConfigureAwait(false);

        await act.Should().ThrowAsync<Exception>()
            .Where(exception => ContainsUniqueViolation(exception)).ConfigureAwait(false);

        await DeleteDocumentAsync(documentStore, seedId).ConfigureAwait(false);

        var retry = await mediator.TrySendAsync(command).ConfigureAwait(false);
        var expectedReceipt = $"receipt:{command.OrderId}:2";

        retry.Outcome.Should().Be(MediationOutcome.Succeeded);
        retry.Value.Should().Be(expectedReceipt);
        counter.Runs.Should().Be(2);

        var stored = await LoadDocumentAsync(documentStore, command.OrderId).ConfigureAwait(false);
        stored.Should().NotBeNull();
        stored!.Receipt.Should().Be(expectedReceipt);

        var keys = await ReadStoredKeysAsync(documentStore).ConfigureAwait(false);
        keys.Should().ContainSingle();
        keys[0].IsCompleted.Should().BeTrue();
        keys[0].Payload.Should().Contain(expectedReceipt);
    }

    [SkippableFact]
    public async Task SendAsync_WithAnUnmarkedStore_WhenTheCommitFails_ShouldKeepTheKeyCompleted()
    {
        var counter = new SettlementHandlerCounter();
        var store = new UnmarkedSettlementIdempotencyStore();
        var provider = BuildProviderWithStore(CreateSchemaName(), store, counter);
        await using var configuredProvider = provider.ConfigureAwait(false);
        var mediator = provider.GetRequiredService<ICommandMediator>();
        var documentStore = provider.GetRequiredService<IDocumentStore>();

        var reference = $"conflict-{Guid.NewGuid():N}";
        var seedId = Guid.NewGuid();
        await SeedDocumentAsync(documentStore, seedId, reference, "seed").ConfigureAwait(false);

        var command = new ApplySettlementOrderCommand(Guid.NewGuid(), reference, FailHandler: false);
        var act = async () => await mediator.SendAsync(command).ConfigureAwait(false);

        await act.Should().ThrowAsync<Exception>()
            .Where(exception => ContainsUniqueViolation(exception)).ConfigureAwait(false);

        var stored = await LoadDocumentAsync(documentStore, command.OrderId).ConfigureAwait(false);
        stored.Should().BeNull();

        // The store settled before the commit, so the failed commit left the completed key behind.
        var firstReceipt = $"receipt:{command.OrderId}:1";
        store.StoredKeys.Should().ContainSingle();
        var settled = store.StoredKeys.Single();
        settled.IsCompleted.Should().BeTrue();
        settled.Payload.Should().Contain(firstReceipt);

        await DeleteDocumentAsync(documentStore, seedId).ConfigureAwait(false);

        var retry = await mediator.TrySendAsync(command).ConfigureAwait(false);
        retry.Outcome.Should().Be(MediationOutcome.Answered);
        retry.Value.Should().Be(firstReceipt);
        counter.Runs.Should().Be(1);

        var missing = await LoadDocumentAsync(documentStore, command.OrderId).ConfigureAwait(false);
        missing.Should().BeNull();
    }

    [SkippableFact]
    public async Task SendAsync_WhenPostCommitSettlementFails_ShouldStillSucceedWithCommittedData()
    {
        var counter = new SettlementHandlerCounter();
        var store = new ThrowingSettlementIdempotencyStore();
        var provider = BuildProviderWithStore(CreateSchemaName(), store, counter);
        await using var configuredProvider = provider.ConfigureAwait(false);
        var mediator = provider.GetRequiredService<ICommandMediator>();
        var documentStore = provider.GetRequiredService<IDocumentStore>();

        var command = new ApplySettlementOrderCommand(Guid.NewGuid(), $"order-{Guid.NewGuid():N}", FailHandler: false);
        var receipt = await mediator.SendAsync(command).ConfigureAwait(false);
        var expectedReceipt = $"receipt:{command.OrderId}:1";

        receipt.Should().Be(expectedReceipt);

        var stored = await LoadDocumentAsync(documentStore, command.OrderId).ConfigureAwait(false);
        stored.Should().NotBeNull();
        stored!.Receipt.Should().Be(expectedReceipt);

        store.StoredKeys.Should().ContainSingle();
        store.StoredKeys.Single().IsCompleted.Should().BeFalse();

        var repeat = await mediator.TrySendAsync(command).ConfigureAwait(false);
        repeat.Outcome.Should().Be(MediationOutcome.Denied);
        repeat.Code.Should().Be(IdempotencyRefusalCodes.InFlight);
    }

    [SkippableFact]
    public async Task SendAsync_WithoutAUnitOfWork_ShouldCompleteTheKey()
    {
        var counter = new SettlementHandlerCounter();
        var provider = BuildProviderWithoutUnitOfWork(CreateSchemaName(), counter);
        await using var configuredProvider = provider.ConfigureAwait(false);
        var mediator = provider.GetRequiredService<ICommandMediator>();
        var documentStore = provider.GetRequiredService<IDocumentStore>();

        var command = new ApplyDirectSettlementOrderCommand(Guid.NewGuid(), $"order-{Guid.NewGuid():N}");
        var receipt = await mediator.SendAsync(command).ConfigureAwait(false);
        var expectedReceipt = $"receipt:{command.OrderId}:1";

        receipt.Should().Be(expectedReceipt);

        var stored = await LoadDocumentAsync(documentStore, command.OrderId).ConfigureAwait(false);
        stored.Should().NotBeNull();
        stored!.Receipt.Should().Be(expectedReceipt);

        var keys = await ReadStoredKeysAsync(documentStore).ConfigureAwait(false);
        keys.Should().ContainSingle();
        keys[0].IsCompleted.Should().BeTrue();
        keys[0].Payload.Should().Contain(expectedReceipt);

        var repeat = await mediator.TrySendAsync(command).ConfigureAwait(false);
        repeat.Outcome.Should().Be(MediationOutcome.Answered);
        repeat.Value.Should().Be(expectedReceipt);
        counter.Runs.Should().Be(1);
    }

    [SkippableFact]
    public async Task SendAsync_WhenTheHandlerFails_ShouldReleaseTheKeyImmediately()
    {
        var counter = new SettlementHandlerCounter();
        var provider = BuildProvider(CreateSchemaName(), counter);
        await using var configuredProvider = provider.ConfigureAwait(false);
        var mediator = provider.GetRequiredService<ICommandMediator>();
        var documentStore = provider.GetRequiredService<IDocumentStore>();

        var orderId = Guid.NewGuid();
        var reference = $"order-{Guid.NewGuid():N}";
        var act = async () => await mediator.SendAsync(
            new ApplySettlementOrderCommand(orderId, reference, FailHandler: true)).ConfigureAwait(false);

        await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);
        counter.Runs.Should().Be(1);

        var keys = await ReadStoredKeysAsync(documentStore).ConfigureAwait(false);
        keys.Should().BeEmpty();

        var missing = await LoadDocumentAsync(documentStore, orderId).ConfigureAwait(false);
        missing.Should().BeNull();

        var retry = await mediator.TrySendAsync(
            new ApplySettlementOrderCommand(orderId, reference, FailHandler: false)).ConfigureAwait(false);
        var expectedReceipt = $"receipt:{orderId}:2";

        retry.Outcome.Should().Be(MediationOutcome.Succeeded);
        retry.Value.Should().Be(expectedReceipt);
        counter.Runs.Should().Be(2);
    }

    /// <summary>
    ///     Creates a database schema name no other test uses.
    /// </summary>
    /// <returns>The schema name.</returns>
    private static string CreateSchemaName()
    {
        return $"msettle{Guid.NewGuid():N}";
    }

    /// <summary>
    ///     Builds a provider with the Marten idempotency store and the Marten unit of work.
    /// </summary>
    /// <param name="schemaName">The database schema the test owns.</param>
    /// <param name="counter">Counts how many times the handler actually ran.</param>
    /// <returns>The configured service provider.</returns>
    private ServiceProvider BuildProvider(string schemaName, SettlementHandlerCounter counter)
    {
        var services = new ServiceCollection();
        services.AddMarten(options =>
        {
            options.Connection(_fixture.ConnectionString);
            options.DatabaseSchemaName = schemaName;
            options.Schema.For<SettlementOrderDocument>().DocumentAlias("settlement_order").UniqueIndex(document => document.Reference);
        });
        services.AddSingleton(counter);
        services.AddLiteBus(registry =>
        {
            registry.AddMessaging(_ => { });
            registry.AddCommands(builder =>
            {
                RegisterSettlementCommands(builder);
                builder.EnableIdempotency();
                builder.UseMartenIdempotencyStore();
                builder.UseMartenUnitOfWork();
            });
        });

        return services.BuildServiceProvider();
    }

    /// <summary>
    ///     Builds a provider with the given idempotency store and the Marten unit of work.
    /// </summary>
    /// <param name="schemaName">The database schema the test owns.</param>
    /// <param name="store">The idempotency store the test observes.</param>
    /// <param name="counter">Counts how many times the handler actually ran.</param>
    /// <returns>The configured service provider.</returns>
    private ServiceProvider BuildProviderWithStore(string schemaName, IIdempotencyStore store, SettlementHandlerCounter counter)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IIdempotencyStore>(store);
        services.AddMarten(options =>
        {
            options.Connection(_fixture.ConnectionString);
            options.DatabaseSchemaName = schemaName;
            options.Schema.For<SettlementOrderDocument>().DocumentAlias("settlement_order").UniqueIndex(document => document.Reference);
        });
        services.AddSingleton(counter);
        services.AddLiteBus(registry =>
        {
            registry.AddMessaging(_ => { });
            registry.AddCommands(builder =>
            {
                RegisterSettlementCommands(builder);
                builder.EnableIdempotency();
                builder.UseMartenUnitOfWork();
            });
        });

        return services.BuildServiceProvider();
    }

    /// <summary>
    ///     Builds a provider with the Marten idempotency store and no unit of work.
    /// </summary>
    /// <param name="schemaName">The database schema the test owns.</param>
    /// <param name="counter">Counts how many times the handler actually ran.</param>
    /// <returns>The configured service provider.</returns>
    private ServiceProvider BuildProviderWithoutUnitOfWork(string schemaName, SettlementHandlerCounter counter)
    {
        var services = new ServiceCollection();
        services.AddMarten(options =>
        {
            options.Connection(_fixture.ConnectionString);
            options.DatabaseSchemaName = schemaName;
            options.Schema.For<SettlementOrderDocument>().DocumentAlias("settlement_order").UniqueIndex(document => document.Reference);
        });
        services.AddSingleton(counter);
        services.AddLiteBus(registry =>
        {
            registry.AddMessaging(_ => { });
            registry.AddCommands(builder =>
            {
                RegisterSettlementCommands(builder);
                builder.EnableIdempotency();
                builder.UseMartenIdempotencyStore();
            });
        });

        return services.BuildServiceProvider();
    }

    /// <summary>
    ///     Registers the settlement-order commands, handlers, and idempotency definitions.
    /// </summary>
    /// <param name="builder">The command module builder.</param>
    private static void RegisterSettlementCommands(CommandModuleBuilder builder)
    {
        builder.Register<ApplySettlementOrderCommand>();
        builder.Register<ApplySettlementOrderCommandHandler>();
        builder.Register<ApplySettlementOrderCommandDefinition>();
        builder.Register<ApplyDirectSettlementOrderCommand>();
        builder.Register<ApplyDirectSettlementOrderCommandHandler>();
        builder.Register<ApplyDirectSettlementOrderCommandDefinition>();
    }

    /// <summary>
    ///     Stores one settlement order document outside any mediation.
    /// </summary>
    /// <param name="documentStore">The document store the test owns.</param>
    /// <param name="orderId">The order identifier.</param>
    /// <param name="reference">The order reference.</param>
    /// <param name="receipt">The receipt recorded on the document.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private static async Task SeedDocumentAsync(IDocumentStore documentStore, Guid orderId, string reference, string receipt)
    {
        var session = documentStore.LightweightSession();
        await using (session.ConfigureAwait(false))
        {
            session.Store(new SettlementOrderDocument { Id = orderId, Reference = reference, Receipt = receipt });

            await session.SaveChangesAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Deletes one settlement order document outside any mediation.
    /// </summary>
    /// <param name="documentStore">The document store the test owns.</param>
    /// <param name="orderId">The order identifier.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private static async Task DeleteDocumentAsync(IDocumentStore documentStore, Guid orderId)
    {
        var session = documentStore.LightweightSession();
        await using (session.ConfigureAwait(false))
        {
            session.Delete<SettlementOrderDocument>(orderId);

            await session.SaveChangesAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Loads one settlement order document outside any mediation.
    /// </summary>
    /// <param name="documentStore">The document store the test owns.</param>
    /// <param name="orderId">The order identifier.</param>
    /// <returns>The stored document, or <see langword="null" /> when no document carries the identifier.</returns>
    private static async Task<SettlementOrderDocument?> LoadDocumentAsync(IDocumentStore documentStore, Guid orderId)
    {
        var session = documentStore.QuerySession();
        await using (session.ConfigureAwait(false))
        {
            return await session.LoadAsync<SettlementOrderDocument>(orderId).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Reads every stored idempotency key from the schema the test owns.
    /// </summary>
    /// <param name="documentStore">The document store the test owns.</param>
    /// <returns>The stored keys and their settlement state.</returns>
    private static async Task<IReadOnlyList<StoredIdempotencyKey>> ReadStoredKeysAsync(IDocumentStore documentStore)
    {
        var session = documentStore.QuerySession();
        await using (session.ConfigureAwait(false))
        {
            var records = await session
                .Query<MartenIdempotencyRecord>()
                .ToListAsync(CancellationToken.None)
                .ConfigureAwait(false);

            return records
                .Select(record => new StoredIdempotencyKey(record.Id, record.IsCompleted, record.Payload))
                .ToList();
        }
    }

    /// <summary>
    ///     Decides whether an exception chain reports a unique-constraint violation.
    /// </summary>
    /// <param name="exception">The exception to inspect.</param>
    /// <returns><see langword="true" /> when a PostgreSQL error in the chain carries state 23505; otherwise, <see langword="false" />.</returns>
    private static bool ContainsUniqueViolation(Exception exception)
    {
        var pending = new Stack<Exception>();
        pending.Push(exception);

        while (pending.Count > 0)
        {
            var current = pending.Pop();

            if (current is PostgresException postgres && postgres.SqlState == UniqueViolationSqlState)
            {
                return true;
            }

            if (current is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions)
                {
                    pending.Push(inner);
                }
            }
            else if (current.InnerException is not null)
            {
                pending.Push(current.InnerException);
            }
        }

        return false;
    }

    /// <summary>
    ///     Counts how many times the test handler actually ran.
    /// </summary>
    public sealed class SettlementHandlerCounter
    {
        /// <summary>
        ///     The number of handler runs recorded so far.
        /// </summary>
        private int _runs;

        /// <summary>
        ///     Gets the number of handler runs recorded so far.
        /// </summary>
        public int Runs => _runs;

        /// <summary>
        ///     Records one handler run.
        /// </summary>
        /// <returns>The one-based run number.</returns>
        public int NextRun()
        {
            return Interlocked.Increment(ref _runs);
        }
    }

    /// <summary>
    ///     A Marten document written by the test handlers.
    /// </summary>
    public sealed class SettlementOrderDocument
    {
        /// <summary>
        ///     Gets or sets the document identity.
        /// </summary>
        public Guid Id { get; set; }

        /// <summary>
        ///     Gets or sets the order reference, which carries a unique index.
        /// </summary>
        public string Reference { get; set; } = string.Empty;

        /// <summary>
        ///     Gets or sets the receipt the handler run recorded.
        /// </summary>
        public string Receipt { get; set; } = string.Empty;
    }

    /// <summary>
    ///     A result-producing command that stages its settlement order for the unit of work to commit.
    /// </summary>
    /// <param name="OrderId">The order identifier used as the idempotency key.</param>
    /// <param name="Reference">The order reference, which carries a unique index.</param>
    /// <param name="FailHandler">Whether the handler throws instead of staging its document.</param>
    public sealed record ApplySettlementOrderCommand(Guid OrderId, string Reference, bool FailHandler) : ICommand<string>;

    /// <summary>
    ///     Stages the settlement order, recording each run on the shared counter.
    /// </summary>
    public sealed class ApplySettlementOrderCommandHandler : ICommandHandler<ApplySettlementOrderCommand, string>
    {
        /// <summary>
        ///     The scoped session the handler stages its document on.
        /// </summary>
        private readonly IDocumentSession _session;

        /// <summary>
        ///     Counts how many times the handler actually ran.
        /// </summary>
        private readonly SettlementHandlerCounter _counter;

        /// <summary>
        ///     Initializes a new instance of the <see cref="ApplySettlementOrderCommandHandler" /> class.
        /// </summary>
        /// <param name="session">The scoped session the handler stages its document on.</param>
        /// <param name="counter">Counts how many times the handler actually ran.</param>
        public ApplySettlementOrderCommandHandler(IDocumentSession session, SettlementHandlerCounter counter)
        {
            _session = session;
            _counter = counter;
        }

        /// <summary>
        ///     Handles the command.
        /// </summary>
        /// <param name="message">The command.</param>
        /// <param name="cancellationToken">A token that cancels the operation.</param>
        /// <returns>The settlement receipt, which differs per run so a replay is distinguishable from a rerun.</returns>
        public Task<string> HandleAsync(ApplySettlementOrderCommand message, CancellationToken cancellationToken = default)
        {
            var run = _counter.NextRun();

            if (message.FailHandler)
            {
                throw new InvalidOperationException("The settlement handler fails for the settlement-order test.");
            }

            var receipt = $"receipt:{message.OrderId}:{run}";
            _session.Store(new SettlementOrderDocument
            {
                Id = message.OrderId,
                Reference = message.Reference,
                Receipt = receipt
            });

            return Task.FromResult(receipt);
        }
    }

    /// <summary>
    ///     Declares the settlement-order command idempotent with result replay.
    /// </summary>
    public sealed class ApplySettlementOrderCommandDefinition : IIdempotencyDefinition<ApplySettlementOrderCommand>
    {
        /// <summary>
        ///     Gets the idempotency declaration for the command.
        /// </summary>
        public IdempotencyDeclaration Idempotency =>
            IdempotencyDeclaration.KeyedBy<ApplySettlementOrderCommand>(command => command.OrderId.ToString("N"))
                with
            { Scope = "settlement-orders", ReplayResult = true };
    }

    /// <summary>
    ///     A result-producing command that saves its settlement order itself, without a unit of work.
    /// </summary>
    /// <param name="OrderId">The order identifier used as the idempotency key.</param>
    /// <param name="Reference">The order reference, which carries a unique index.</param>
    public sealed record ApplyDirectSettlementOrderCommand(Guid OrderId, string Reference) : ICommand<string>;

    /// <summary>
    ///     Saves the settlement order directly, recording each run on the shared counter.
    /// </summary>
    public sealed class ApplyDirectSettlementOrderCommandHandler : ICommandHandler<ApplyDirectSettlementOrderCommand, string>
    {
        /// <summary>
        ///     The scoped session the handler saves its document through.
        /// </summary>
        private readonly IDocumentSession _session;

        /// <summary>
        ///     Counts how many times the handler actually ran.
        /// </summary>
        private readonly SettlementHandlerCounter _counter;

        /// <summary>
        ///     Initializes a new instance of the <see cref="ApplyDirectSettlementOrderCommandHandler" /> class.
        /// </summary>
        /// <param name="session">The scoped session the handler saves its document through.</param>
        /// <param name="counter">Counts how many times the handler actually ran.</param>
        public ApplyDirectSettlementOrderCommandHandler(IDocumentSession session, SettlementHandlerCounter counter)
        {
            _session = session;
            _counter = counter;
        }

        /// <summary>
        ///     Handles the command.
        /// </summary>
        /// <param name="message">The command.</param>
        /// <param name="cancellationToken">A token that cancels the operation.</param>
        /// <returns>The settlement receipt, which differs per run so a replay is distinguishable from a rerun.</returns>
        public async Task<string> HandleAsync(ApplyDirectSettlementOrderCommand message, CancellationToken cancellationToken = default)
        {
            var run = _counter.NextRun();
            var receipt = $"receipt:{message.OrderId}:{run}";
            _session.Store(new SettlementOrderDocument
            {
                Id = message.OrderId,
                Reference = message.Reference,
                Receipt = receipt
            });

            await _session.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return receipt;
        }
    }

    /// <summary>
    ///     Declares the direct settlement-order command idempotent with result replay.
    /// </summary>
    public sealed class ApplyDirectSettlementOrderCommandDefinition : IIdempotencyDefinition<ApplyDirectSettlementOrderCommand>
    {
        /// <summary>
        ///     Gets the idempotency declaration for the command.
        /// </summary>
        public IdempotencyDeclaration Idempotency =>
            IdempotencyDeclaration.KeyedBy<ApplyDirectSettlementOrderCommand>(command => command.OrderId.ToString("N"))
                with
            { Scope = "settlement-orders", ReplayResult = true };
    }

    /// <summary>
    ///     The stored state of one idempotency key observed during a settlement-order test.
    /// </summary>
    /// <param name="Key">The stored key.</param>
    /// <param name="IsCompleted">Whether the key was marked applied.</param>
    /// <param name="Payload">The serialized result recorded for replay, or <see langword="null" /> when none.</param>
    public sealed record StoredIdempotencyKey(string Key, bool IsCompleted, string? Payload);

    /// <summary>
    ///     An in-memory idempotency store without the separately-committed marker, so LiteBus settles it before the unit of work commits.
    /// </summary>
    /// <remarks>
    ///     Claims never expire: each test settles its keys before finishing, so no takeover path is needed.
    /// </remarks>
    public sealed class UnmarkedSettlementIdempotencyStore : IIdempotencyStore
    {
        /// <summary>
        ///     One key's remembered state.
        /// </summary>
        /// <param name="Fingerprint">The fingerprint the claiming delivery sent, or <see langword="null" /> when none.</param>
        /// <param name="IsCompleted">Whether the key was marked applied.</param>
        /// <param name="Payload">The serialized result recorded for replay, or <see langword="null" /> when none.</param>
        /// <param name="ClaimToken">The token fencing the current claim.</param>
        private sealed record ClaimEntry(string? Fingerprint, bool IsCompleted, string? Payload, string ClaimToken);

        /// <summary>
        ///     Guards the remembered keys, so a claim is checked and recorded atomically.
        /// </summary>
        private readonly object _entriesLock = new();

        /// <summary>
        ///     The remembered keys and their state.
        /// </summary>
        private readonly Dictionary<string, ClaimEntry> _entries = new(StringComparer.Ordinal);

        /// <summary>
        ///     Gets the stored state of every remembered key.
        /// </summary>
        public IReadOnlyCollection<StoredIdempotencyKey> StoredKeys
        {
            get
            {
                lock (_entriesLock)
                {
                    return _entries
                        .Select(entry => new StoredIdempotencyKey(entry.Key, entry.Value.IsCompleted, entry.Value.Payload))
                        .ToList();
                }
            }
        }

        /// <inheritdoc />
        public Task<IdempotencyClaim> TryClaimAsync(string key, string? fingerprint, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);

            lock (_entriesLock)
            {
                if (!_entries.TryGetValue(key, out var existing))
                {
                    var granted = Guid.NewGuid().ToString("N");
                    _entries[key] = new ClaimEntry(fingerprint, IsCompleted: false, Payload: null, ClaimToken: granted);

                    return Task.FromResult(IdempotencyClaim.Granted(granted));
                }

                if (existing.IsCompleted)
                {
                    if (existing.Fingerprint is not null
                        && fingerprint is not null
                        && !string.Equals(existing.Fingerprint, fingerprint, StringComparison.Ordinal))
                    {
                        return Task.FromResult(IdempotencyClaim.FingerprintMismatch);
                    }

                    return Task.FromResult(IdempotencyClaim.AlreadyCompleted(existing.Payload));
                }

                return Task.FromResult(IdempotencyClaim.InFlight);
            }
        }

        /// <inheritdoc />
        public Task CompleteAsync(string key, string claimToken, string? payload, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            ArgumentException.ThrowIfNullOrWhiteSpace(claimToken);

            lock (_entriesLock)
            {
                if (_entries.TryGetValue(key, out var existing)
                    && string.Equals(existing.ClaimToken, claimToken, StringComparison.Ordinal))
                {
                    _entries[key] = existing with { IsCompleted = true, Payload = payload };
                }
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task ReleaseAsync(string key, string claimToken, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            ArgumentException.ThrowIfNullOrWhiteSpace(claimToken);

            lock (_entriesLock)
            {
                if (_entries.TryGetValue(key, out var existing)
                    && !existing.IsCompleted
                    && string.Equals(existing.ClaimToken, claimToken, StringComparison.Ordinal))
                {
                    _entries.Remove(key);
                }
            }

            return Task.CompletedTask;
        }
    }

    /// <summary>
    ///     An in-memory idempotency store that declares separately-committed writes and fails every settlement, so the test proves a post-commit settlement failure does not fail the mediation.
    /// </summary>
    /// <remarks>
    ///     Claims never expire: each test settles its keys before finishing, so no takeover path is needed.
    /// </remarks>
    public sealed class ThrowingSettlementIdempotencyStore : ISeparatelyCommittedIdempotencyStore
    {
        /// <summary>
        ///     One key's remembered state.
        /// </summary>
        /// <param name="Fingerprint">The fingerprint the claiming delivery sent, or <see langword="null" /> when none.</param>
        /// <param name="IsCompleted">Whether the key was marked applied.</param>
        /// <param name="Payload">The serialized result recorded for replay, or <see langword="null" /> when none.</param>
        /// <param name="ClaimToken">The token fencing the current claim.</param>
        private sealed record ClaimEntry(string? Fingerprint, bool IsCompleted, string? Payload, string ClaimToken);

        /// <summary>
        ///     Guards the remembered keys, so a claim is checked and recorded atomically.
        /// </summary>
        private readonly object _entriesLock = new();

        /// <summary>
        ///     The remembered keys and their state.
        /// </summary>
        private readonly Dictionary<string, ClaimEntry> _entries = new(StringComparer.Ordinal);

        /// <summary>
        ///     Gets the stored state of every remembered key.
        /// </summary>
        public IReadOnlyCollection<StoredIdempotencyKey> StoredKeys
        {
            get
            {
                lock (_entriesLock)
                {
                    return _entries
                        .Select(entry => new StoredIdempotencyKey(entry.Key, entry.Value.IsCompleted, entry.Value.Payload))
                        .ToList();
                }
            }
        }

        /// <inheritdoc />
        public Task<IdempotencyClaim> TryClaimAsync(string key, string? fingerprint, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);

            lock (_entriesLock)
            {
                if (!_entries.TryGetValue(key, out var existing))
                {
                    var granted = Guid.NewGuid().ToString("N");
                    _entries[key] = new ClaimEntry(fingerprint, IsCompleted: false, Payload: null, ClaimToken: granted);

                    return Task.FromResult(IdempotencyClaim.Granted(granted));
                }

                if (existing.IsCompleted)
                {
                    if (existing.Fingerprint is not null
                        && fingerprint is not null
                        && !string.Equals(existing.Fingerprint, fingerprint, StringComparison.Ordinal))
                    {
                        return Task.FromResult(IdempotencyClaim.FingerprintMismatch);
                    }

                    return Task.FromResult(IdempotencyClaim.AlreadyCompleted(existing.Payload));
                }

                return Task.FromResult(IdempotencyClaim.InFlight);
            }
        }

        /// <inheritdoc />
        public Task CompleteAsync(string key, string claimToken, string? payload, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            ArgumentException.ThrowIfNullOrWhiteSpace(claimToken);

            throw new InvalidOperationException("Post-commit settlement fails for the settlement-order test.");
        }

        /// <inheritdoc />
        public Task ReleaseAsync(string key, string claimToken, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            ArgumentException.ThrowIfNullOrWhiteSpace(claimToken);

            lock (_entriesLock)
            {
                if (_entries.TryGetValue(key, out var existing)
                    && !existing.IsCompleted
                    && string.Equals(existing.ClaimToken, claimToken, StringComparison.Ordinal))
                {
                    _entries.Remove(key);
                }
            }

            return Task.CompletedTask;
        }
    }
}
