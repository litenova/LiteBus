using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.EntityFrameworkCore;
using LiteBus.Runtime.Abstractions.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Storage.UnitTests.EntityFrameworkCore.ContextRegistration;

/// <summary>
///     Verifies resolving a transactional outbox for a context with no UseDbContext call fails with the missing call named.
/// </summary>
public sealed class UnconfiguredTransactionalOutboxTests
{
    /// <summary>
    ///     Confirms resolving the outbox for an unconfigured context throws with the missing UseDbContext call named.
    /// </summary>
    [Fact]
    public void GetService_UnconfiguredContext_ShouldThrowNamingUseDbContext()
    {
        using var provider = new ServiceCollection()
            .AddDbContextFactory<OrdersDbContext>(options => options.UseInMemoryDatabase(Guid.NewGuid().ToString("N")))
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ =>
                {
                });

                registry.AddOutbox(outbox => outbox.UseEntityFrameworkCoreStorage(builder => builder
                    .UseDbContext<OrdersDbContext>()
                    .EnableSaveChangesInterceptor()));
            })
            .BuildServiceProvider();

        using var scope = provider.CreateScope();

        var act = () => scope.ServiceProvider.GetService<ITransactionalOutbox<BillingDbContext>>();

        act.Should().Throw<LiteBusConfigurationException>()
            .WithMessage("*UseDbContext<BillingDbContext>*");
    }

    /// <summary>
    ///     Confirms resolving the outbox for the configured context still resolves the transactional writer.
    /// </summary>
    [Fact]
    public void GetService_ConfiguredContext_ShouldResolveTransactionalOutbox()
    {
        var databaseName = Guid.NewGuid().ToString("N");

        using var provider = new ServiceCollection()
            .AddDbContextFactory<OrdersDbContext>(options => options.UseInMemoryDatabase(databaseName))
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ =>
                {
                });

                registry.AddOutbox(outbox => outbox.UseEntityFrameworkCoreStorage(builder => builder
                    .UseDbContext<OrdersDbContext>()
                    .EnableSaveChangesInterceptor()));
            })
            .AddDbContext<OrdersDbContext>(options => options.UseInMemoryDatabase(databaseName))
            .BuildServiceProvider();

        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<ITransactionalOutbox<OrdersDbContext>>()
            .Should().BeOfType<TransactionalOutbox<OrdersDbContext>>();
    }

    private sealed class OrdersDbContext : DbContext, IOutboxDbContext
    {
        public OrdersDbContext(DbContextOptions<OrdersDbContext> options)
            : base(options)
        {
        }

        public DbSet<OutboxMessageEntity> OutboxMessages => Set<OutboxMessageEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.GetModelBuilderConfiguration();
        }
    }

    private sealed class BillingDbContext : DbContext
    {
        public BillingDbContext(DbContextOptions<BillingDbContext> options)
            : base(options)
        {
        }
    }
}
