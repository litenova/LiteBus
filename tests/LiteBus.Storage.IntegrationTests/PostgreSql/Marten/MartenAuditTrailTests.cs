using LiteBus.Commands;
using System.Data;
using LiteBus.Commands.Abstractions;
using LiteBus.Commands.Storage.Marten;
using LiteBus.Messaging.Abstractions;
using LiteBus.Storage.IntegrationTests.PostgreSql;
using Marten;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Storage.IntegrationTests.PostgreSql.Marten;

/// <summary>
///     Integration tests for the Marten audit trail: success records commit with the handler change, rolled-back sessions lose staged records, and non-success records persist out of band.
/// </summary>
public sealed class MartenAuditTrailTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="MartenAuditTrailTests" /> class.
    /// </summary>
    /// <param name="fixture">The shared PostgreSQL container fixture.</param>
    public MartenAuditTrailTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
        Skip.IfNot(_fixture.IsAvailable, PostgreSqlFixture.DockerRequiredMessage);
    }

    [SkippableFact]
    public async Task WriteAsync_ForSuccess_ShouldCommitWithTheHandlerChange()
    {
        var documentStore = CreateDocumentStore();
        await using var configuredDocumentStore = documentStore.ConfigureAwait(false);
        var action = $"orders.place-{Guid.NewGuid():N}";
        var orderId = Guid.NewGuid();

        var session = documentStore.LightweightSession(IsolationLevel.ReadCommitted);
        await using (session.ConfigureAwait(false))
        {
            var trail = new MartenAuditTrail(session, documentStore);
            await trail.WriteAsync(SuccessRecord(action)).ConfigureAwait(false);
            session.Store(new OrderDocument { Id = orderId, Total = 7m });
            await session.SaveChangesAsync().ConfigureAwait(false);
        }

        var stored = await LoadAsync(documentStore, orderId).ConfigureAwait(false);
        stored.Should().NotBeNull();

        var audit = await FindAuditAsync(documentStore, action).ConfigureAwait(false);
        audit.Should().NotBeNull();
        audit!.Outcome.Should().Be(AuditOutcome.Succeeded);
        audit.Action.Should().Be(action);
    }

    [SkippableFact]
    public async Task WriteAsync_ForSuccess_WhenSessionRollsBack_ShouldLoseTheRecord()
    {
        var documentStore = CreateDocumentStore();
        await using var configuredDocumentStore = documentStore.ConfigureAwait(false);
        var action = $"orders.place-{Guid.NewGuid():N}";

        var session = documentStore.LightweightSession(IsolationLevel.ReadCommitted);
        await using (session.ConfigureAwait(false))
        {
            var trail = new MartenAuditTrail(session, documentStore);
            await trail.WriteAsync(SuccessRecord(action)).ConfigureAwait(false);
        }

        var audit = await FindAuditAsync(documentStore, action).ConfigureAwait(false);
        audit.Should().BeNull();
    }

    [SkippableFact]
    public async Task WriteAsync_ForFailure_ShouldPersistOutOfBand()
    {
        var documentStore = CreateDocumentStore();
        await using var configuredDocumentStore = documentStore.ConfigureAwait(false);
        var action = $"orders.place-{Guid.NewGuid():N}";
        var orderId = Guid.NewGuid();

        var session = documentStore.LightweightSession(IsolationLevel.ReadCommitted);
        await using (session.ConfigureAwait(false))
        {
            session.Store(new OrderDocument { Id = orderId, Total = 7m });
            var trail = new MartenAuditTrail(session, documentStore);
            await trail
                .WriteAsync(new AuditRecord { Action = action, Outcome = AuditOutcome.Failed, OccurredAt = DateTimeOffset.UtcNow })
                .ConfigureAwait(false);
        }

        var stored = await LoadAsync(documentStore, orderId).ConfigureAwait(false);
        stored.Should().BeNull();

        var audit = await FindAuditAsync(documentStore, action).ConfigureAwait(false);
        audit.Should().NotBeNull();
        audit!.Outcome.Should().Be(AuditOutcome.Failed);
    }

    [SkippableFact]
    public async Task SendAsync_ForAuditedCommand_ShouldCommitTheAuditRecordWithTheChange()
    {
        var provider = BuildProvider();
        await using var configuredProvider = provider.ConfigureAwait(false);
        var mediator = provider.GetRequiredService<ICommandMediator>();
        var orderId = Guid.NewGuid();

        var result = await mediator.TrySendAsync(new AuditedPlaceOrderCommand(orderId, 9m)).ConfigureAwait(false);

        result.IsSuccess.Should().BeTrue();

        var documentStore = provider.GetRequiredService<IDocumentStore>();
        var stored = await LoadAsync(documentStore, orderId).ConfigureAwait(false);
        stored.Should().NotBeNull();

        var audit = await FindAuditAsync(documentStore, "orders.place-order").ConfigureAwait(false);
        audit.Should().NotBeNull();
        audit!.Outcome.Should().Be(AuditOutcome.Succeeded);
    }

    [SkippableFact]
    public async Task TrySendAsync_WhenGuardDenies_ShouldRecordTheDenialAndRollBackTheChange()
    {
        var provider = BuildProvider();
        await using var configuredProvider = provider.ConfigureAwait(false);
        var mediator = provider.GetRequiredService<ICommandMediator>();
        var orderId = Guid.NewGuid();

        var result = await mediator.TrySendAsync(new AuditedBlockedOrderCommand(orderId)).ConfigureAwait(false);

        result.Outcome.Should().Be(MediationOutcome.Denied);

        var documentStore = provider.GetRequiredService<IDocumentStore>();
        var stored = await LoadAsync(documentStore, orderId).ConfigureAwait(false);
        stored.Should().BeNull();

        var audit = await FindAuditAsync(documentStore, "orders.blocked-order").ConfigureAwait(false);
        audit.Should().NotBeNull();
        audit!.Outcome.Should().Be(AuditOutcome.Denied);
    }

    private DocumentStore CreateDocumentStore()
    {
        return DocumentStore.For(options =>
        {
            options.Connection(_fixture.ConnectionString);
            options.DatabaseSchemaName = "marten_commands_audit_tests";
        });
    }

    private ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();

        services.AddMarten(options =>
        {
            options.Connection(_fixture.ConnectionString);
            options.DatabaseSchemaName = "marten_commands_audit_tests";
        });

        services.AddLiteBus(registry =>
        {
            registry.AddMessaging(messaging => messaging.UseAuditTrail<MartenAuditTrail>());

            registry.AddCommands(commands =>
            {
                commands.Register<AuditedPlaceOrderCommand>();
                commands.Register<AuditedPlaceOrderCommandHandler>();
                commands.Register<AuditedBlockedOrderCommand>();
                commands.Register<AuditedBlockedOrderCommandHandler>();
                commands.Register<AuditedBlockedOrderGuard>();
                commands.EnableAuditing();
                commands.UseMartenUnitOfWork();
            });
        });

        return services.BuildServiceProvider();
    }

    private static AuditRecord SuccessRecord(string action)
    {
        return new AuditRecord { Action = action, Outcome = AuditOutcome.Succeeded, OccurredAt = DateTimeOffset.UtcNow };
    }

    private static async Task<OrderDocument?> LoadAsync(IDocumentStore documentStore, Guid orderId)
    {
        var session = documentStore.QuerySession();
        await using (session.ConfigureAwait(false))
        {
            return await session.LoadAsync<OrderDocument>(orderId).ConfigureAwait(false);
        }
    }

    private static async Task<MartenAuditRecord?> FindAuditAsync(IDocumentStore documentStore, string action)
    {
        var session = documentStore.QuerySession();
        await using (session.ConfigureAwait(false))
        {
            var records = await session
                .Query<MartenAuditRecord>()
                .Where(record => record.Action == action)
                .ToListAsync(CancellationToken.None)
                .ConfigureAwait(false);

            return records.SingleOrDefault();
        }
    }

    /// <summary>
    ///     A Marten document written by the test handlers.
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
    ///     An audited command whose handler stores its document on the scoped session.
    /// </summary>
    /// <param name="OrderId">The order identifier used as the document identity.</param>
    /// <param name="Total">The order total written to the document.</param>
    [Audited("orders.place-order")]
    public sealed record AuditedPlaceOrderCommand(Guid OrderId, decimal Total) : ICommand<string>;

    /// <summary>
    ///     Stores the order document, which the unit of work commits with the audit record.
    /// </summary>
    public sealed class AuditedPlaceOrderCommandHandler : ICommandHandler<AuditedPlaceOrderCommand, string>
    {
        /// <summary>
        ///     The scoped session the handler stages its document on.
        /// </summary>
        private readonly IDocumentSession _session;

        /// <summary>
        ///     Initializes a new instance of the <see cref="AuditedPlaceOrderCommandHandler" /> class.
        /// </summary>
        /// <param name="session">The scoped session the handler stages its document on.</param>
        public AuditedPlaceOrderCommandHandler(IDocumentSession session)
        {
            _session = session;
        }

        /// <summary>
        ///     Handles the command.
        /// </summary>
        /// <param name="message">The command.</param>
        /// <param name="cancellationToken">A token that cancels the operation.</param>
        /// <returns>The settlement receipt.</returns>
        public Task<string> HandleAsync(AuditedPlaceOrderCommand message, CancellationToken cancellationToken = default)
        {
            _session.Store(new OrderDocument { Id = message.OrderId, Total = message.Total });
            return Task.FromResult($"receipt:{message.OrderId}");
        }
    }

    /// <summary>
    ///     An audited command the test guard always denies.
    /// </summary>
    /// <param name="OrderId">The order identifier used as the document identity.</param>
    [Audited("orders.blocked-order")]
    public sealed record AuditedBlockedOrderCommand(Guid OrderId) : ICommand<string>;

    /// <summary>
    ///     Would store the order document, but never runs because the guard denies first.
    /// </summary>
    public sealed class AuditedBlockedOrderCommandHandler : ICommandHandler<AuditedBlockedOrderCommand, string>
    {
        /// <summary>
        ///     The scoped session the handler would stage its document on.
        /// </summary>
        private readonly IDocumentSession _session;

        /// <summary>
        ///     Initializes a new instance of the <see cref="AuditedBlockedOrderCommandHandler" /> class.
        /// </summary>
        /// <param name="session">The scoped session the handler would stage its document on.</param>
        public AuditedBlockedOrderCommandHandler(IDocumentSession session)
        {
            _session = session;
        }

        /// <summary>
        ///     Handles the command.
        /// </summary>
        /// <param name="message">The command.</param>
        /// <param name="cancellationToken">A token that cancels the operation.</param>
        /// <returns>The settlement receipt.</returns>
        public Task<string> HandleAsync(AuditedBlockedOrderCommand message, CancellationToken cancellationToken = default)
        {
            _session.Store(new OrderDocument { Id = message.OrderId, Total = 1m });
            return Task.FromResult($"receipt:{message.OrderId}");
        }
    }

    /// <summary>
    ///     Stages a document and then denies, so the test proves a denial is recorded while the change rolls back.
    /// </summary>
    public sealed class AuditedBlockedOrderGuard : ICommandGuard<AuditedBlockedOrderCommand>
    {
        /// <summary>
        ///     The scoped session the guard stages its document on.
        /// </summary>
        private readonly IDocumentSession _session;

        /// <summary>
        ///     Initializes a new instance of the <see cref="AuditedBlockedOrderGuard" /> class.
        /// </summary>
        /// <param name="session">The scoped session the guard stages its document on.</param>
        public AuditedBlockedOrderGuard(IDocumentSession session)
        {
            _session = session;
        }

        /// <summary>
        ///     Decides whether the message is permitted to proceed.
        /// </summary>
        /// <param name="message">The message being mediated.</param>
        /// <param name="cancellationToken">The cancellation token supplied to the mediation operation.</param>
        /// <returns>The verdict that denies the message.</returns>
        public Task<Verdict> DecideAsync(AuditedBlockedOrderCommand message, CancellationToken cancellationToken = default)
        {
            _session.Store(new OrderDocument { Id = message.OrderId, Total = 1m });
            return Task.FromResult(Verdict.Deny("Blocked for the audit trail test."));
        }
    }
}
