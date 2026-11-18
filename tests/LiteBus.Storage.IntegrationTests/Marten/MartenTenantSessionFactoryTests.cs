using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Commands.Storage.Marten;
using LiteBus.Messaging.Abstractions;
using LiteBus.Storage.IntegrationTests.PostgreSql;
using Marten;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Storage.IntegrationTests.Marten;

/// <summary>
///     Integration tests for the Marten tenant session factory: tenant isolation, default sessions, and composition.
/// </summary>
public sealed class MartenTenantSessionFactoryTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="MartenTenantSessionFactoryTests" /> class.
    /// </summary>
    /// <param name="fixture">The shared PostgreSQL container fixture.</param>
    public MartenTenantSessionFactoryTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
        Skip.IfNot(_fixture.IsAvailable, PostgreSqlFixture.DockerRequiredMessage);
    }

    [SkippableFact]
    public async Task Tenant_sessions_isolate_documents_by_tenant()
    {
        var documentStore = CreateDocumentStore("marten_commands_tenant_session_tests");
        await using var configuredDocumentStore = documentStore.ConfigureAwait(false);
        var factory = new MartenTenantSessionFactory(documentStore);
        var orderId = Guid.NewGuid();

        using (AmbientExecutionContext.CreateScope(new TestExecutionContext(ExecutionIdentity.ForTenant("tenant-a"))))
        {
            var session = factory.OpenSession();
            await using (session.ConfigureAwait(false))
            {
                session.Store(new TenantOrderDocument { Id = orderId, Total = 7m });
                await session.SaveChangesAsync().ConfigureAwait(false);
            }
        }

        TenantOrderDocument? hidden;
        using (AmbientExecutionContext.CreateScope(new TestExecutionContext(ExecutionIdentity.ForTenant("tenant-b"))))
        {
            var session = factory.OpenQuerySession();
            await using (session.ConfigureAwait(false))
            {
                hidden = await session.LoadAsync<TenantOrderDocument>(orderId).ConfigureAwait(false);
            }
        }

        hidden.Should().BeNull();

        TenantOrderDocument? visible;
        using (AmbientExecutionContext.CreateScope(new TestExecutionContext(ExecutionIdentity.ForTenant("tenant-a"))))
        {
            var session = factory.OpenQuerySession();
            await using (session.ConfigureAwait(false))
            {
                visible = await session.LoadAsync<TenantOrderDocument>(orderId).ConfigureAwait(false);
            }
        }

        visible.Should().NotBeNull();
        visible!.Total.Should().Be(7m);
    }

    [SkippableFact]
    public async Task Default_session_without_an_ambient_mediation_stores_and_reads()
    {
        var documentStore = CreateDocumentStore("marten_commands_tenant_session_tests");
        await using var configuredDocumentStore = documentStore.ConfigureAwait(false);
        var factory = new MartenTenantSessionFactory(documentStore);
        var orderId = Guid.NewGuid();

        var session = factory.OpenSession();
        await using (session.ConfigureAwait(false))
        {
            session.Store(new TenantOrderDocument { Id = orderId, Total = 11m });
            await session.SaveChangesAsync().ConfigureAwait(false);
        }

        var query = factory.OpenQuerySession();
        await using (query.ConfigureAwait(false))
        {
            var stored = await query.LoadAsync<TenantOrderDocument>(orderId).ConfigureAwait(false);
            stored.Should().NotBeNull();
            stored!.Total.Should().Be(11m);
        }
    }

    [SkippableFact]
    public async Task Composed_factory_opens_tenant_sessions()
    {
        var provider = BuildProvider();
        await using var configuredProvider = provider.ConfigureAwait(false);
        var factory = provider.GetRequiredService<MartenTenantSessionFactory>();
        var orderId = Guid.NewGuid();

        using (AmbientExecutionContext.CreateScope(new TestExecutionContext(ExecutionIdentity.ForTenant("tenant-a"))))
        {
            var session = factory.OpenSession();
            await using (session.ConfigureAwait(false))
            {
                session.TenantId.Should().Be("tenant-a");
                session.Store(new TenantOrderDocument { Id = orderId, Total = 5m });
                await session.SaveChangesAsync().ConfigureAwait(false);
            }

            var query = factory.OpenQuerySession();
            await using (query.ConfigureAwait(false))
            {
                var stored = await query.LoadAsync<TenantOrderDocument>(orderId).ConfigureAwait(false);
                stored.Should().NotBeNull();
            }
        }
    }

    private DocumentStore CreateDocumentStore(string schemaName)
    {
        return DocumentStore.For(options =>
        {
            options.Connection(_fixture.ConnectionString);
            options.DatabaseSchemaName = schemaName;
            options.Policies.AllDocumentsAreMultiTenanted();
            options.MultiTenantedWithSingleServer(
                _fixture.ConnectionString,
                tenants => tenants.WithTenants(["tenant-a", "tenant-b"]));
        });
    }

    private ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();

        services.AddMarten(options =>
        {
            options.Connection(_fixture.ConnectionString);
            options.DatabaseSchemaName = "marten_commands_tenant_module_tests";
            options.Policies.AllDocumentsAreMultiTenanted();
            options.MultiTenantedWithSingleServer(
                _fixture.ConnectionString,
                tenants => tenants.WithTenants(["tenant-a", "tenant-b"]));
        });

        services.AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddCommands(commands =>
            {
                commands.Register<TenantPingCommand>();
                commands.Register<TenantPingCommandHandler>();
                commands.UseMartenTenantSessions();
            });
        });

        return services.BuildServiceProvider();
    }

    /// <summary>
    ///     A Marten document stored through tenant sessions.
    /// </summary>
    public sealed class TenantOrderDocument
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
    ///     A command that exists so the tenant session composition has a handler to register.
    /// </summary>
    /// <param name="OrderId">The order identifier carried by the command.</param>
    public sealed record TenantPingCommand(Guid OrderId) : ICommand<string>;

    /// <summary>
    ///     Answers the composition probe without touching Marten.
    /// </summary>
    public sealed class TenantPingCommandHandler : ICommandHandler<TenantPingCommand, string>
    {
        /// <summary>
        ///     Handles the command.
        /// </summary>
        /// <param name="message">The command.</param>
        /// <param name="cancellationToken">A token that cancels the operation.</param>
        /// <returns>A fixed receipt.</returns>
        public Task<string> HandleAsync(TenantPingCommand message, CancellationToken cancellationToken = default)
        {
            return Task.FromResult($"pong:{message.OrderId}");
        }
    }

    private sealed class TestExecutionContext : IExecutionContext
    {
        public TestExecutionContext(ExecutionIdentity identity)
        {
            Identity = identity;
        }

        public CancellationToken CancellationToken => CancellationToken.None;

        public DateTimeOffset HandledAt { get; } = new DateTimeOffset(2026, 5, 4, 12, 0, 0, TimeSpan.Zero);

        public ExecutionIdentity Identity { get; }

        public IDictionary<string, object> Items { get; } = new Dictionary<string, object>();

        public IHandleContextData Data { get; } = new HandleContextData();

        public IReadOnlyCollection<string> Tags => [];

        public object? MessageResult { get; set; }

        public bool PostHandlersSuppressed => false;

        public void SuppressPostHandlers()
        {
        }
    }
}
