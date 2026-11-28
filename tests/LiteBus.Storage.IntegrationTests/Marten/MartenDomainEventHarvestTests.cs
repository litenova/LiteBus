using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.DurableMessaging;
using LiteBus.Outbox;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.Marten;
using LiteBus.Outbox.Storage.PostgreSql;
using LiteBus.Storage.IntegrationTests.PostgreSql;
using Marten;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Storage.IntegrationTests.Marten;

/// <summary>
///     Verifies domain events buffered on Marten documents harvest into the outbox before collected events stage.
/// </summary>
public sealed class MartenDomainEventHarvestTests : IClassFixture<PostgreSqlFixture>
{
    /// <summary>
    ///     The contract name staged events are registered under.
    /// </summary>
    private const string ContractName = "tests.events.order-placed";

    /// <summary>
    ///     The PostgreSQL fixture shared across tests.
    /// </summary>
    private readonly PostgreSqlFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="MartenDomainEventHarvestTests" /> class.
    /// </summary>
    /// <param name="fixture">The PostgreSQL fixture.</param>
    public MartenDomainEventHarvestTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
        Skip.IfNot(_fixture.IsAvailable, PostgreSqlFixture.DockerRequiredMessage);
    }

    /// <summary>
    ///     Verifies an inserted document that implements the domain event contract stages its events and clears them.
    /// </summary>
    [SkippableFact]
    public async Task Inserted_document_implementing_the_contract_stages_and_clears()
    {
        var outboxOptions = await CreateOutboxSchemaAsync().ConfigureAwait(false);
        var probe = new HarvestProbe();
        var provider = BuildProvider(outboxOptions, probe);
        await using var configuredProvider = provider.ConfigureAwait(false);
        var orderId = Guid.NewGuid();

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new SubmitOrderCommand(orderId, HarvestScenario.DefaultContract)).ConfigureAwait(false);

        var manager = provider.GetRequiredService<IOutboxManager>();
        var page = await manager.QueryAsync(
            new OutboxMessageFilter { ContractName = ContractName },
            new OutboxMessagePageRequest { PageSize = 10 }).ConfigureAwait(false);

        page.Items.Should().HaveCount(2);
        page.Items.Should().OnlyContain(envelope => envelope.Status == OutboxStatus.Pending);
        probe.HarvestedAggregates.Should().HaveCount(1);
        probe.HarvestedAggregates[0].Should().BeOfType<OrderDocument>();
        ((OrderDocument) probe.HarvestedAggregates[0]).DomainEvents.Should().BeEmpty();

        var documentStore = provider.GetRequiredService<IDocumentStore>();
        var querySession = documentStore.QuerySession();
        await using var configuredQuerySession = querySession.ConfigureAwait(false);
        var stored = await querySession.LoadAsync<OrderDocument>(orderId).ConfigureAwait(false);

        stored.Should().NotBeNull();
    }

    /// <summary>
    ///     Verifies an inserted document with configured accessors stages its events and clears them.
    /// </summary>
    [SkippableFact]
    public async Task Inserted_document_with_configured_accessors_stages_and_clears()
    {
        var outboxOptions = await CreateOutboxSchemaAsync().ConfigureAwait(false);
        var probe = new HarvestProbe();
        var provider = BuildProvider(outboxOptions, probe);
        await using var configuredProvider = provider.ConfigureAwait(false);
        var orderId = Guid.NewGuid();

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new SubmitOrderCommand(orderId, HarvestScenario.ConfiguredAccessor)).ConfigureAwait(false);

        var manager = provider.GetRequiredService<IOutboxManager>();
        var page = await manager.QueryAsync(
            new OutboxMessageFilter { ContractName = ContractName },
            new OutboxMessagePageRequest { PageSize = 10 }).ConfigureAwait(false);

        page.Items.Should().HaveCount(1);
        page.Items.Should().OnlyContain(envelope => envelope.Status == OutboxStatus.Pending);
        probe.HarvestedAggregates.Should().HaveCount(1);
        probe.HarvestedAggregates[0].Should().BeOfType<CustomOrderDocument>();
        ((CustomOrderDocument) probe.HarvestedAggregates[0]).PendingEvents.Should().BeEmpty();

        var documentStore = provider.GetRequiredService<IDocumentStore>();
        var querySession = documentStore.QuerySession();
        await using var configuredQuerySession = querySession.ConfigureAwait(false);
        var stored = await querySession.LoadAsync<CustomOrderDocument>(orderId).ConfigureAwait(false);

        stored.Should().NotBeNull();
    }

    /// <summary>
    ///     Verifies an updated document stages the events it raised.
    /// </summary>
    [SkippableFact]
    public async Task Updated_document_stages_its_events()
    {
        var outboxOptions = await CreateOutboxSchemaAsync().ConfigureAwait(false);
        var probe = new HarvestProbe();
        var provider = BuildProvider(outboxOptions, probe);
        await using var configuredProvider = provider.ConfigureAwait(false);
        var orderId = Guid.NewGuid();

        var seedScope = provider.CreateAsyncScope();
        await using (seedScope.ConfigureAwait(false))
        {
            var session = seedScope.ServiceProvider.GetRequiredService<IDocumentSession>();
            session.Store(new OrderDocument { Id = orderId, Total = 10m });
            await session.SaveChangesAsync().ConfigureAwait(false);
        }

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new SubmitOrderCommand(orderId, HarvestScenario.Update)).ConfigureAwait(false);

        var manager = provider.GetRequiredService<IOutboxManager>();
        var page = await manager.QueryAsync(
            new OutboxMessageFilter { ContractName = ContractName },
            new OutboxMessagePageRequest { PageSize = 10 }).ConfigureAwait(false);

        page.Items.Should().HaveCount(1);
        page.Items.Should().OnlyContain(envelope => envelope.Status == OutboxStatus.Pending);
    }

    /// <summary>
    ///     Verifies a deleted document stages its final events before its row is removed.
    /// </summary>
    [SkippableFact]
    public async Task Deleted_document_stages_its_final_events()
    {
        var outboxOptions = await CreateOutboxSchemaAsync().ConfigureAwait(false);
        var probe = new HarvestProbe();
        var provider = BuildProvider(outboxOptions, probe);
        await using var configuredProvider = provider.ConfigureAwait(false);
        var orderId = Guid.NewGuid();

        var seedScope = provider.CreateAsyncScope();
        await using (seedScope.ConfigureAwait(false))
        {
            var session = seedScope.ServiceProvider.GetRequiredService<IDocumentSession>();
            session.Store(new OrderDocument { Id = orderId, Total = 10m });
            await session.SaveChangesAsync().ConfigureAwait(false);
        }

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new SubmitOrderCommand(orderId, HarvestScenario.Delete)).ConfigureAwait(false);

        var manager = provider.GetRequiredService<IOutboxManager>();
        var page = await manager.QueryAsync(
            new OutboxMessageFilter { ContractName = ContractName },
            new OutboxMessagePageRequest { PageSize = 10 }).ConfigureAwait(false);

        page.Items.Should().HaveCount(1);
        page.Items.Should().OnlyContain(envelope => envelope.Status == OutboxStatus.Pending);
        probe.HarvestedAggregates.Should().HaveCount(1);
        probe.HarvestedAggregates[0].Should().BeOfType<OrderDocument>();
        ((OrderDocument) probe.HarvestedAggregates[0]).DomainEvents.Should().BeEmpty();

        var documentStore = provider.GetRequiredService<IDocumentStore>();
        var querySession = documentStore.QuerySession();
        await using var configuredQuerySession = querySession.ConfigureAwait(false);
        var stored = await querySession.LoadAsync<OrderDocument>(orderId).ConfigureAwait(false);

        stored.Should().BeNull();
    }

    /// <summary>
    ///     Verifies a predicate deletion stages nothing, because it carries no document.
    /// </summary>
    [SkippableFact]
    public async Task DeleteWhere_deletion_carries_no_document_and_stages_nothing()
    {
        var outboxOptions = await CreateOutboxSchemaAsync().ConfigureAwait(false);
        var probe = new HarvestProbe();
        var provider = BuildProvider(outboxOptions, probe);
        await using var configuredProvider = provider.ConfigureAwait(false);
        var orderId = Guid.NewGuid();

        var seedScope = provider.CreateAsyncScope();
        await using (seedScope.ConfigureAwait(false))
        {
            var session = seedScope.ServiceProvider.GetRequiredService<IDocumentSession>();
            session.Store(new OrderDocument { Id = orderId, Total = 10m });
            await session.SaveChangesAsync().ConfigureAwait(false);
        }

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new SubmitOrderCommand(orderId, HarvestScenario.DeleteWhere)).ConfigureAwait(false);

        var manager = provider.GetRequiredService<IOutboxManager>();
        var page = await manager.QueryAsync(
            new OutboxMessageFilter { ContractName = ContractName },
            new OutboxMessagePageRequest { PageSize = 10 }).ConfigureAwait(false);

        page.Items.Should().BeEmpty();
        probe.HarvestedAggregates.Should().HaveCount(1);
        probe.HarvestedAggregates[0].Should().BeOfType<OrderDocument>();
        ((OrderDocument) probe.HarvestedAggregates[0]).DomainEvents.Should().HaveCount(1);

        var documentStore = provider.GetRequiredService<IDocumentStore>();
        var querySession = documentStore.QuerySession();
        await using var configuredQuerySession = querySession.ConfigureAwait(false);
        var stored = await querySession.LoadAsync<OrderDocument>(orderId).ConfigureAwait(false);

        stored.Should().BeNull();
    }

    /// <summary>
    ///     Builds one provider over a fresh outbox table with Marten domain event harvesting enabled.
    /// </summary>
    /// <param name="outboxOptions">The outbox store options for one test run.</param>
    /// <param name="probe">The probe that captures harvested aggregates.</param>
    /// <returns>The configured service provider.</returns>
    private ServiceProvider BuildProvider(PostgreSqlOutboxStoreOptions outboxOptions, HarvestProbe probe)
    {
        var services = new ServiceCollection();
        services.AddSingleton(_fixture.DataSource);
        services.AddSingleton(probe);
        services.AddMarten(options =>
        {
            options.Connection(_fixture.ConnectionString);
            options.DatabaseSchemaName = "marten_domain_event_harvest_tests";
            options.Schema.For<OrderDocument>().Identity(document => document.Id);
            options.Schema.For<CustomOrderDocument>().Identity(document => document.Id);
        });

        services.AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddCommands(commands =>
            {
                commands.Register<SubmitOrderCommand>();
                commands.Register<SubmitOrderCommandHandler>();
                commands.Register<MartenSaveChangesObserver>();
            });

            registry.AddOutbox(outbox =>
            {
                outbox.Contracts.Register<OrderPlacedHarvestEvent>(ContractName);
                outbox.HarvestDomainEvents<CustomOrderDocument>(
                    document => document.PendingEvents,
                    document => document.ClearPendingEvents());

                outbox.UsePostgreSqlStorage(pg =>
                {
                    pg.UseDataSource(_fixture.DataSource)
                        .UseOptions(outboxOptions)
                        .DisableSchemaInitialization()
                        .UseMartenTransactionalOutbox()
                        .HarvestDomainEvents();
                });

                outbox.StageCollectedEvents();
            });
        });

        return services.BuildServiceProvider();
    }

    /// <summary>
    ///     Creates a fresh outbox table for one test run.
    /// </summary>
    /// <returns>The outbox store options for one test run.</returns>
    private async Task<PostgreSqlOutboxStoreOptions> CreateOutboxSchemaAsync()
    {
        var outboxOptions = PostgreSqlTestInfrastructure.CreateOutboxStoreOptions(
            $"outbox_marten_harvest_{Guid.NewGuid():N}");

        await PostgreSqlTestInfrastructure
            .EnsureOutboxSchemaAsync(_fixture.DataSource, outboxOptions)
            .ConfigureAwait(false);

        return outboxOptions;
    }

    /// <summary>
    ///     Selects which document operation the harvest command handler performs.
    /// </summary>
    public enum HarvestScenario
    {
        /// <summary>
        ///     Stores a new document that implements the domain event contract.
        /// </summary>
        DefaultContract,

        /// <summary>
        ///     Stores a new document read through configured accessors.
        /// </summary>
        ConfiguredAccessor,

        /// <summary>
        ///     Loads a stored document, changes it, and raises an event.
        /// </summary>
        Update,

        /// <summary>
        ///     Loads a stored document, raises an event, and deletes it.
        /// </summary>
        Delete,

        /// <summary>
        ///     Raises an event on an untracked document and deletes the stored row by predicate.
        /// </summary>
        DeleteWhere
    }

    /// <summary>
    ///     Stores or mutates order documents for one harvest scenario.
    /// </summary>
    /// <param name="OrderId">The order identifier carried by the command.</param>
    /// <param name="Scenario">The document operation the handler performs.</param>
    public sealed record SubmitOrderCommand(Guid OrderId, HarvestScenario Scenario) : ICommand<string>;

    /// <summary>
    ///     Stores or mutates order documents for the scenario the command names.
    /// </summary>
    public sealed class SubmitOrderCommandHandler : ICommandHandler<SubmitOrderCommand, string>
    {
        /// <summary>
        ///     The probe that captures harvested aggregates.
        /// </summary>
        private readonly HarvestProbe _probe;

        /// <summary>
        ///     The document session the handler stages documents on.
        /// </summary>
        private readonly IDocumentSession _session;

        /// <summary>
        ///     Initializes a new instance of the <see cref="SubmitOrderCommandHandler" /> class.
        /// </summary>
        /// <param name="session">The document session the handler stages documents on.</param>
        /// <param name="probe">The probe that captures harvested aggregates.</param>
        public SubmitOrderCommandHandler(IDocumentSession session, HarvestProbe probe)
        {
            _session = session;
            _probe = probe;
        }

        /// <summary>
        ///     Handles the command by storing or mutating documents for its scenario.
        /// </summary>
        /// <param name="message">The command.</param>
        /// <param name="cancellationToken">A token that cancels the operation.</param>
        /// <returns>A receipt naming the stored order.</returns>
        public async Task<string> HandleAsync(SubmitOrderCommand message, CancellationToken cancellationToken = default)
        {
            switch (message.Scenario)
            {
                case HarvestScenario.DefaultContract:
                    {
                        var aggregate = new OrderDocument { Id = message.OrderId, Total = 42m };
                        aggregate.Raise(new OrderPlacedHarvestEvent { OrderId = message.OrderId });
                        aggregate.Raise(new OrderPlacedHarvestEvent { OrderId = message.OrderId });
                        _session.Store(aggregate);
                        _probe.HarvestedAggregates.Add(aggregate);
                        break;
                    }

                case HarvestScenario.ConfiguredAccessor:
                    {
                        var aggregate = new CustomOrderDocument { Id = message.OrderId, Total = 42m };
                        aggregate.PendingEvents.Add(new OrderPlacedHarvestEvent { OrderId = message.OrderId });
                        _session.Store(aggregate);
                        _probe.HarvestedAggregates.Add(aggregate);
                        break;
                    }

                case HarvestScenario.Update:
                    {
                        var stored = await _session.LoadAsync<OrderDocument>(message.OrderId, cancellationToken).ConfigureAwait(false);
                        stored!.Total = 84m;
                        stored.Raise(new OrderPlacedHarvestEvent { OrderId = message.OrderId });
                        _session.Store(stored);
                        _probe.HarvestedAggregates.Add(stored);
                        break;
                    }

                case HarvestScenario.Delete:
                    {
                        var stored = await _session.LoadAsync<OrderDocument>(message.OrderId, cancellationToken).ConfigureAwait(false);
                        stored!.Raise(new OrderPlacedHarvestEvent { OrderId = message.OrderId });
                        _session.Delete(stored);
                        _probe.HarvestedAggregates.Add(stored);
                        break;
                    }

                case HarvestScenario.DeleteWhere:
                    {
                        var aggregate = new OrderDocument { Id = message.OrderId, Total = 10m };
                        aggregate.Raise(new OrderPlacedHarvestEvent { OrderId = message.OrderId });
                        _session.DeleteWhere<OrderDocument>(document => document.Id == message.OrderId);
                        _probe.HarvestedAggregates.Add(aggregate);
                        break;
                    }

                default:
                    throw new InvalidOperationException($"Unknown harvest scenario '{message.Scenario}'.");
            }

            return $"stored:{message.OrderId}";
        }
    }

    /// <summary>
    ///     Saves the scoped document session after staging, so staged rows commit with the documents.
    /// </summary>
    [HandlerPriority(HandlerPriorities.UnitOfWork)]
    public sealed class MartenSaveChangesObserver : ICommandCompletionHandler<SubmitOrderCommand>
    {
        /// <summary>
        ///     The document session saved when the mediation succeeds.
        /// </summary>
        private readonly IDocumentSession _session;

        /// <summary>
        ///     Initializes a new instance of the <see cref="MartenSaveChangesObserver" /> class.
        /// </summary>
        /// <param name="session">The document session saved when the mediation succeeds.</param>
        public MartenSaveChangesObserver(IDocumentSession session)
        {
            _session = session;
        }

        /// <summary>
        ///     Saves the session when the mediation completed successfully.
        /// </summary>
        /// <param name="context">The typed completion context observed at the end of mediation.</param>
        /// <param name="cancellationToken">The cancellation token for the completion stage.</param>
        /// <returns>A task representing the asynchronous completion-handling operation.</returns>
        public async Task HandleCompletionAsync(
            MessageCompletionContext<SubmitOrderCommand> context,
            CancellationToken cancellationToken)
        {
            if (context.Outcome != MediationOutcome.Succeeded)
            {
                return;
            }

            await _session.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Captures the aggregates the command handler staged, so tests can verify harvesting cleared them.
    /// </summary>
    public sealed class HarvestProbe
    {
        /// <summary>
        ///     Gets the aggregates the command handler staged.
        /// </summary>
        public List<object> HarvestedAggregates { get; } = [];
    }

    /// <summary>
    ///     A Marten document that buffers its domain events through the shared contract.
    /// </summary>
    public sealed class OrderDocument : IHasDomainEvents
    {
        /// <summary>
        ///     The buffered domain events raised since the last harvest.
        /// </summary>
        private readonly List<object> _events = [];

        /// <summary>
        ///     Gets or sets the document identity.
        /// </summary>
        public Guid Id { get; set; }

        /// <summary>
        ///     Gets or sets the order total.
        /// </summary>
        public decimal Total { get; set; }

        /// <summary>
        ///     Gets the domain events the document raised and has not yet handed over.
        /// </summary>
        public IReadOnlyCollection<object> DomainEvents => _events;

        /// <summary>
        ///     Buffers one domain event for the next harvest.
        /// </summary>
        /// <param name="domainEvent">The domain event to buffer.</param>
        public void Raise(object domainEvent)
        {
            _events.Add(domainEvent);
        }

        /// <summary>
        ///     Removes every buffered domain event, after a harvester collected them.
        /// </summary>
        public void ClearDomainEvents()
        {
            _events.Clear();
        }
    }

    /// <summary>
    ///     A Marten document that buffers its domain events in its own list, read through configured accessors.
    /// </summary>
    public sealed class CustomOrderDocument
    {
        /// <summary>
        ///     Gets or sets the document identity.
        /// </summary>
        public Guid Id { get; set; }

        /// <summary>
        ///     Gets or sets the order total.
        /// </summary>
        public decimal Total { get; set; }

        /// <summary>
        ///     Gets the buffered domain events raised since the last harvest.
        /// </summary>
        public List<object> PendingEvents { get; } = [];

        /// <summary>
        ///     Removes every buffered domain event, after a harvester collected them.
        /// </summary>
        public void ClearPendingEvents()
        {
            PendingEvents.Clear();
        }
    }

    /// <summary>
    ///     The domain event harvested from order documents.
    /// </summary>
    public sealed record OrderPlacedHarvestEvent
    {
        /// <summary>
        ///     Gets the placed order identifier.
        /// </summary>
        public required Guid OrderId { get; init; }
    }
}
