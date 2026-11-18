using LiteBus.Outbox.Storage.EntityFrameworkCore;
using LiteBus.Outbox.Storage.EntityFrameworkCore.Startup;
using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Runtime.Abstractions.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Storage.UnitTests.EntityFrameworkCore.ContextRegistration;

/// <summary>
///     Verifies the EF Core outbox module fails host startup when the configured database context has no scoped registration.
/// </summary>
public sealed class EfCoreOutboxDbContextStartupCheckTests
{
    /// <summary>
    ///     Confirms composition records the database context startup check.
    /// </summary>
    [Fact]
    public void AddEfCoreOutboxStorage_ShouldRecordDbContextStartupCheck()
    {
        using var provider = new ServiceCollection()
            .AddDbContextFactory<OrdersDbContext>(options => options.UseInMemoryDatabase(Guid.NewGuid().ToString("N")))
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ =>
                {
                });

                registry.AddOutbox(outbox => outbox.UseEntityFrameworkCoreStorage(builder =>
                    builder.UseDbContext<OrdersDbContext>()));
            })
            .BuildServiceProvider();

        provider.GetRequiredService<LiteBusHostManifest>().StartupTasks
            .Should().Contain(typeof(EfCoreOutboxDbContextStartupCheck<OrdersDbContext>));
    }

    /// <summary>
    ///     Confirms the check fails with the missing AddDbContext call named when only a factory is registered.
    /// </summary>
    [Fact]
    public async Task RunAsync_WithoutScopedContextRegistration_ShouldThrowNamingAddDbContext()
    {
        using var provider = new ServiceCollection()
            .AddDbContextFactory<OrdersDbContext>(options => options.UseInMemoryDatabase(Guid.NewGuid().ToString("N")))
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ =>
                {
                });

                registry.AddOutbox(outbox => outbox.UseEntityFrameworkCoreStorage(builder =>
                    builder.UseDbContext<OrdersDbContext>()));
            })
            .BuildServiceProvider();

        var check = new EfCoreOutboxDbContextStartupCheck<OrdersDbContext>(provider);

        var act = async () => await check.RunAsync(CancellationToken.None).ConfigureAwait(false);

        var thrown = await act.Should().ThrowAsync<LiteBusConfigurationException>().ConfigureAwait(false);
        thrown.WithMessage("*AddDbContext<OrdersDbContext>*");
    }

    /// <summary>
    ///     Confirms the check completes when the scoped context is registered.
    /// </summary>
    [Fact]
    public async Task RunAsync_WithScopedContextRegistration_ShouldComplete()
    {
        var databaseName = Guid.NewGuid().ToString("N");

        using var provider = new ServiceCollection()
            .AddDbContextFactory<OrdersDbContext>(options => options.UseInMemoryDatabase(databaseName))
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ =>
                {
                });

                registry.AddOutbox(outbox => outbox.UseEntityFrameworkCoreStorage(builder =>
                    builder.UseDbContext<OrdersDbContext>()));
            })
            .AddDbContext<OrdersDbContext>(options => options.UseInMemoryDatabase(databaseName))
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        var check = new EfCoreOutboxDbContextStartupCheck<OrdersDbContext>(provider);

        await check.RunAsync(CancellationToken.None).ConfigureAwait(false);
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
}
