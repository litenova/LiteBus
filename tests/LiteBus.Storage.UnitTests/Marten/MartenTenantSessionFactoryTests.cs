using System.Data;
using LiteBus.Commands.Storage.Marten;
using LiteBus.Messaging.Abstractions;
using Marten;

namespace LiteBus.Storage.UnitTests.Marten;

/// <summary>
///     Verifies that the Marten tenant session factory opens sessions for the ambient tenant. These tests open
///     sessions only and never touch the database.
/// </summary>
public sealed class MartenTenantSessionFactoryTests
{
    [Fact]
    public void ResolveTenantId_without_an_ambient_mediation_is_null()
    {
        MartenTenantSessionFactory.ResolveTenantId().Should().BeNull();
    }

    [Fact]
    public void ResolveTenantId_for_an_anonymous_mediation_is_null()
    {
        using var scope = AmbientExecutionContext.CreateScope(new TestExecutionContext(ExecutionIdentity.Anonymous));

        MartenTenantSessionFactory.ResolveTenantId().Should().BeNull();
    }

    [Fact]
    public void ResolveTenantId_for_a_tenant_mediation_is_the_tenant()
    {
        using var scope = AmbientExecutionContext.CreateScope(
            new TestExecutionContext(ExecutionIdentity.ForTenant("tenant-a")));

        MartenTenantSessionFactory.ResolveTenantId().Should().Be("tenant-a");
    }

    [Fact]
    public async Task OpenSession_for_a_tenant_mediation_opens_the_tenant_session()
    {
        var store = CreateDocumentStore();
        await using var configuredStore = store.ConfigureAwait(false);
        using var scope = AmbientExecutionContext.CreateScope(
            new TestExecutionContext(ExecutionIdentity.ForTenant("tenant-a")));

        var factory = new MartenTenantSessionFactory(store);
        var session = factory.OpenSession();

        await using (session.ConfigureAwait(false))
        {
            session.TenantId.Should().Be("tenant-a");
        }
    }

    [Fact]
    public async Task OpenSession_without_an_ambient_mediation_opens_the_default_session()
    {
        var store = CreateDocumentStore();
        await using var configuredStore = store.ConfigureAwait(false);

        var factory = new MartenTenantSessionFactory(store);
        var session = factory.OpenSession();
        var expected = store.LightweightSession(IsolationLevel.ReadCommitted);

        await using (session.ConfigureAwait(false))
        {
            await using (expected.ConfigureAwait(false))
            {
                session.TenantId.Should().Be(expected.TenantId);
            }
        }
    }

    [Fact]
    public async Task OpenSession_for_a_blank_tenant_opens_the_default_session()
    {
        var store = CreateDocumentStore();
        await using var configuredStore = store.ConfigureAwait(false);
        using var scope = AmbientExecutionContext.CreateScope(
            new TestExecutionContext(new ExecutionIdentity { TenantId = "  " }));

        var factory = new MartenTenantSessionFactory(store);
        var session = factory.OpenSession();
        var expected = store.LightweightSession(IsolationLevel.ReadCommitted);

        await using (session.ConfigureAwait(false))
        {
            await using (expected.ConfigureAwait(false))
            {
                session.TenantId.Should().Be(expected.TenantId);
            }
        }
    }

    [Fact]
    public async Task OpenQuerySession_for_a_tenant_mediation_opens_the_tenant_session()
    {
        var store = CreateDocumentStore();
        await using var configuredStore = store.ConfigureAwait(false);
        using var scope = AmbientExecutionContext.CreateScope(
            new TestExecutionContext(ExecutionIdentity.ForTenant("tenant-b")));

        var factory = new MartenTenantSessionFactory(store);
        var session = factory.OpenQuerySession();

        await using (session.ConfigureAwait(false))
        {
            session.TenantId.Should().Be("tenant-b");
        }
    }

    [Fact]
    public void Null_document_store_is_rejected()
    {
        var factory = () => new MartenTenantSessionFactory(null!);
        factory.Should().Throw<ArgumentNullException>();
    }

    private static DocumentStore CreateDocumentStore()
    {
        // Conjoined tenancy keeps every tenant in one database: opening a session only selects the tenant
        // filter, so the connection string below is never dialled and the default tenant stays available.
        const string connectionString =
            "Host=localhost;Port=1;Database=marten_tenant_factory_tests;Username=postgres;Password=postgres";

        return DocumentStore.For(options =>
        {
            options.Connection(connectionString);
            options.Policies.AllDocumentsAreMultiTenanted();
        });
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
