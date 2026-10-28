using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Commands.Storage.Marten;
using LiteBus.Messaging.Abstractions;
using LiteBus.Storage.IntegrationTests.PostgreSql;
using Marten;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Storage.IntegrationTests.PostgreSql.Marten;

/// <summary>
///     Integration tests for the Marten unit of work: the handler session commits on success and rolls back on denial or failure.
/// </summary>
public sealed class MartenUnitOfWorkTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="MartenUnitOfWorkTests" /> class.
    /// </summary>
    /// <param name="fixture">The shared PostgreSQL container fixture.</param>
    public MartenUnitOfWorkTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
        Skip.IfNot(_fixture.IsAvailable, PostgreSqlFixture.DockerRequiredMessage);
    }

    [SkippableFact]
    public async Task SendAsync_ShouldCommitTheHandlerDocument()
    {
        var provider = BuildProvider();
        await using var configuredProvider = provider.ConfigureAwait(false);
        var mediator = provider.GetRequiredService<ICommandMediator>();
        var orderId = Guid.NewGuid();

        var result = await mediator.TrySendAsync(new PlaceOrderCommand(orderId, 42m)).ConfigureAwait(false);

        result.IsSuccess.Should().BeTrue();
        var stored = await LoadAsync(provider.GetRequiredService<IDocumentStore>(), orderId).ConfigureAwait(false);
        stored.Should().NotBeNull();
        stored!.Total.Should().Be(42m);
    }

    [SkippableFact]
    public async Task TrySendAsync_WhenGuardDenies_ShouldCommitNothing()
    {
        var provider = BuildProvider();
        await using var configuredProvider = provider.ConfigureAwait(false);
        var mediator = provider.GetRequiredService<ICommandMediator>();
        var orderId = Guid.NewGuid();

        var result = await mediator.TrySendAsync(new BlockedOrderCommand(orderId)).ConfigureAwait(false);

        result.Outcome.Should().Be(MediationOutcome.Denied);
        var stored = await LoadAsync(provider.GetRequiredService<IDocumentStore>(), orderId).ConfigureAwait(false);
        stored.Should().BeNull();
    }

    [SkippableFact]
    public async Task TrySendAsync_WhenHandlerThrows_ShouldCommitNothing()
    {
        var provider = BuildProvider();
        await using var configuredProvider = provider.ConfigureAwait(false);
        var mediator = provider.GetRequiredService<ICommandMediator>();
        var orderId = Guid.NewGuid();

        // TrySendAsync returns decisions as values; a genuine fault still throws.
        var act = () => mediator.TrySendAsync(new FailingOrderCommand(orderId));

        await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);
        var stored = await LoadAsync(provider.GetRequiredService<IDocumentStore>(), orderId).ConfigureAwait(false);
        stored.Should().BeNull();
    }

    private ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();

        services.AddMarten(options =>
        {
            options.Connection(_fixture.ConnectionString);
            options.DatabaseSchemaName = "marten_commands_uow_tests";
        });

        services.AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddCommands(commands =>
            {
                commands.Register<PlaceOrderCommand>();
                commands.Register<PlaceOrderCommandHandler>();
                commands.Register<BlockedOrderCommand>();
                commands.Register<BlockedOrderCommandHandler>();
                commands.Register<BlockedOrderGuard>();
                commands.Register<FailingOrderCommand>();
                commands.Register<FailingOrderCommandHandler>();
                commands.UseMartenUnitOfWork();
            });
        });

        return services.BuildServiceProvider();
    }

    private static async Task<OrderDocument?> LoadAsync(IDocumentStore documentStore, Guid orderId)
    {
        var session = documentStore.QuerySession();
        await using (session.ConfigureAwait(false))
        {
            return await session.LoadAsync<OrderDocument>(orderId).ConfigureAwait(false);
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
    ///     A command whose handler stores its document on the scoped session.
    /// </summary>
    /// <param name="OrderId">The order identifier used as the document identity.</param>
    /// <param name="Total">The order total written to the document.</param>
    public sealed record PlaceOrderCommand(Guid OrderId, decimal Total) : ICommand<string>;

    /// <summary>
    ///     Stores the order document, which the unit of work commits on success.
    /// </summary>
    public sealed class PlaceOrderCommandHandler : ICommandHandler<PlaceOrderCommand, string>
    {
        /// <summary>
        ///     The scoped session the handler stages its document on.
        /// </summary>
        private readonly IDocumentSession _session;

        /// <summary>
        ///     Initializes a new instance of the <see cref="PlaceOrderCommandHandler" /> class.
        /// </summary>
        /// <param name="session">The scoped session the handler stages its document on.</param>
        public PlaceOrderCommandHandler(IDocumentSession session)
        {
            _session = session;
        }

        /// <summary>
        ///     Handles the command.
        /// </summary>
        /// <param name="message">The command.</param>
        /// <param name="cancellationToken">A token that cancels the operation.</param>
        /// <returns>The settlement receipt.</returns>
        public Task<string> HandleAsync(PlaceOrderCommand message, CancellationToken cancellationToken = default)
        {
            _session.Store(new OrderDocument { Id = message.OrderId, Total = message.Total });
            return Task.FromResult($"receipt:{message.OrderId}");
        }
    }

    /// <summary>
    ///     A command the test guard always denies.
    /// </summary>
    /// <param name="OrderId">The order identifier used as the document identity.</param>
    public sealed record BlockedOrderCommand(Guid OrderId) : ICommand<string>;

    /// <summary>
    ///     Would store the order document, but never runs because the guard denies first.
    /// </summary>
    public sealed class BlockedOrderCommandHandler : ICommandHandler<BlockedOrderCommand, string>
    {
        /// <summary>
        ///     The scoped session the handler would stage its document on.
        /// </summary>
        private readonly IDocumentSession _session;

        /// <summary>
        ///     Initializes a new instance of the <see cref="BlockedOrderCommandHandler" /> class.
        /// </summary>
        /// <param name="session">The scoped session the handler would stage its document on.</param>
        public BlockedOrderCommandHandler(IDocumentSession session)
        {
            _session = session;
        }

        /// <summary>
        ///     Handles the command.
        /// </summary>
        /// <param name="message">The command.</param>
        /// <param name="cancellationToken">A token that cancels the operation.</param>
        /// <returns>The settlement receipt.</returns>
        public Task<string> HandleAsync(BlockedOrderCommand message, CancellationToken cancellationToken = default)
        {
            _session.Store(new OrderDocument { Id = message.OrderId, Total = 1m });
            return Task.FromResult($"receipt:{message.OrderId}");
        }
    }

    /// <summary>
    ///     Stages a document and then denies, so the test proves a denial commits nothing staged.
    /// </summary>
    public sealed class BlockedOrderGuard : ICommandGuard<BlockedOrderCommand>
    {
        /// <summary>
        ///     The scoped session the guard stages its document on.
        /// </summary>
        private readonly IDocumentSession _session;

        /// <summary>
        ///     Initializes a new instance of the <see cref="BlockedOrderGuard" /> class.
        /// </summary>
        /// <param name="session">The scoped session the guard stages its document on.</param>
        public BlockedOrderGuard(IDocumentSession session)
        {
            _session = session;
        }

        /// <summary>
        ///     Decides whether the message is permitted to proceed.
        /// </summary>
        /// <param name="message">The message being mediated.</param>
        /// <param name="cancellationToken">The cancellation token supplied to the mediation operation.</param>
        /// <returns>The verdict that denies the message.</returns>
        public Task<Verdict> DecideAsync(BlockedOrderCommand message, CancellationToken cancellationToken = default)
        {
            _session.Store(new OrderDocument { Id = message.OrderId, Total = 1m });
            return Task.FromResult(Verdict.Deny("Blocked for the unit-of-work test."));
        }
    }

    /// <summary>
    ///     A command whose handler stores its document and then throws.
    /// </summary>
    /// <param name="OrderId">The order identifier used as the document identity.</param>
    public sealed record FailingOrderCommand(Guid OrderId) : ICommand<string>;

    /// <summary>
    ///     Stages the order document and then throws, so the test proves a failure commits nothing staged.
    /// </summary>
    public sealed class FailingOrderCommandHandler : ICommandHandler<FailingOrderCommand, string>
    {
        /// <summary>
        ///     The scoped session the handler stages its document on.
        /// </summary>
        private readonly IDocumentSession _session;

        /// <summary>
        ///     Initializes a new instance of the <see cref="FailingOrderCommandHandler" /> class.
        /// </summary>
        /// <param name="session">The scoped session the handler stages its document on.</param>
        public FailingOrderCommandHandler(IDocumentSession session)
        {
            _session = session;
        }

        /// <summary>
        ///     Handles the command.
        /// </summary>
        /// <param name="message">The command.</param>
        /// <param name="cancellationToken">A token that cancels the operation.</param>
        /// <returns>The settlement receipt.</returns>
        public Task<string> HandleAsync(FailingOrderCommand message, CancellationToken cancellationToken = default)
        {
            _session.Store(new OrderDocument { Id = message.OrderId, Total = 1m });
            throw new InvalidOperationException("Failing for the unit-of-work test.");
        }
    }
}
