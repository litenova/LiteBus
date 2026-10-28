using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Storage.UnitTests.EntityFrameworkCore.Outbox;

/// <summary>
///     Verifies the EF Core outbox module registers a non-generic transactional writer over the typed writer.
/// </summary>
public sealed class TransactionalOutboxAdapterTests
{
    /// <summary>
    ///     Confirms the interceptor registration resolves a working non-generic transactional outbox.
    /// </summary>
    [Fact]
    public async Task GetService_WithInterceptorEnabled_ShouldResolveWorkingNonGenericTransactionalOutbox()
    {
        var databaseName = Guid.NewGuid().ToString("N");

        var provider = new ServiceCollection()
            .AddDbContextFactory<AdapterOutboxDbContext>(options => options.UseInMemoryDatabase(databaseName))
            .AddLiteBus(liteBus =>
            {
                liteBus.AddMessaging(_ =>
                {
                });

                liteBus.AddOutbox(outbox =>
                {
                    outbox.Contracts.Register<OrderCollectedEvent>("orders.events.collected", 1);
                    outbox.UseEntityFrameworkCoreStorage(builder => builder
                        .UseDbContext<AdapterOutboxDbContext>()
                        .EnableSaveChangesInterceptor());
                });
            })
            .AddDbContext<AdapterOutboxDbContext>((serviceProvider, options) => options
                .UseInMemoryDatabase(databaseName)
                .AddLiteBusOutboxInterceptor(serviceProvider.GetRequiredService<LiteBusOutboxSaveChangesInterceptor>()))
            .BuildServiceProvider();

        using var scope = provider.CreateScope();

        var outbox = scope.ServiceProvider.GetService<ITransactionalOutbox>();
        outbox.Should().NotBeNull();

        var receipt = await outbox!.EnqueueAsync(
            OutboxEnqueueItem<OrderCollectedEvent>.From(new OrderCollectedEvent { OrderId = Guid.NewGuid() })).ConfigureAwait(false);
        receipt.Contract.Name.Should().Be("orders.events.collected");

        var context = scope.ServiceProvider.GetRequiredService<AdapterOutboxDbContext>();
        await context.SaveChangesAsync().ConfigureAwait(false);

        (await context.OutboxMessages.CountAsync().ConfigureAwait(false)).Should().Be(1);
    }

    /// <summary>
    ///     Confirms no non-generic transactional outbox is registered without the save-changes interceptor.
    /// </summary>
    [Fact]
    public void GetService_WithoutInterceptorEnabled_ShouldNotResolveNonGenericTransactionalOutbox()
    {
        var provider = new ServiceCollection()
            .AddDbContextFactory<AdapterOutboxDbContext>(options => options.UseInMemoryDatabase(Guid.NewGuid().ToString("N")))
            .AddLiteBus(liteBus =>
            {
                liteBus.AddMessaging(_ =>
                {
                });

                liteBus.AddOutbox(outbox => outbox.UseEntityFrameworkCoreStorage(builder =>
                    builder.UseDbContext<AdapterOutboxDbContext>()));
            })
            .BuildServiceProvider();

        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetService<ITransactionalOutbox>().Should().BeNull();
    }

    private sealed record OrderCollectedEvent
    {
        public Guid OrderId { get; init; }
    }

    private sealed class AdapterOutboxDbContext : DbContext, IOutboxDbContext
    {
        public AdapterOutboxDbContext(DbContextOptions<AdapterOutboxDbContext> options)
            : base(options)
        {
        }

        public DbSet<OutboxMessageEntity> OutboxMessages => Set<OutboxMessageEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.GetModelBuilderConfiguration();
        }
    }
}
