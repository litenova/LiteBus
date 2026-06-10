using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Outbox;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.EntityFrameworkCore;
using LiteBus.Runtime.Abstractions.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Outbox.Storage.EntityFrameworkCore.UnitTests;

public sealed class EfCoreOutboxStorageModuleTests
{
    [Fact]
    public void AddEfCoreOutboxStorage_ShouldRegisterSingleStoreInstanceForAllRoles()
    {
        var databaseName = Guid.NewGuid().ToString("N");

        var provider = new ServiceCollection()
            .AddDbContext<ModuleTestOutboxDbContext>(options => options.UseInMemoryDatabase(databaseName))
            .AddLiteBus(registry =>
            {
                registry.AddMessageModule(_ => { });
                registry.AddOutboxModule(outbox => outbox.UseEfCoreStorage(builder =>
                    builder.UseDbContext<ModuleTestOutboxDbContext>()));
            })
            .BuildServiceProvider();

        var store = provider.GetRequiredService<EfCoreOutboxStore>();
        provider.GetRequiredService<IOutboxStore>().Should().BeSameAs(store);
        provider.GetRequiredService<IOutboxLeaseStore>().Should().BeSameAs(store);
        provider.GetRequiredService<IOutboxStateWriter>().Should().BeSameAs(store);
        provider.GetRequiredService<IOutboxDeadLetterStore>().Should().BeSameAs(store);
        provider.GetRequiredService<IOutboxRetentionStore>().Should().BeSameAs(store);
        provider.GetRequiredService<IOutboxDiagnosticsStore>().Should().BeSameAs(store);
    }

    [Fact]
    public void AddEfCoreOutboxStorage_WithEnforceTransactionalSetupWithoutInterceptor_ShouldThrowOnBuild()
    {
        var act = () =>
            new ServiceCollection()
                .AddDbContext<ModuleTestOutboxDbContext>(options => options.UseInMemoryDatabase(Guid.NewGuid().ToString("N")))
                .AddLiteBus(registry =>
                {
                    registry.AddMessageModule(_ => { });
                    registry.AddOutboxModule(outbox => outbox.UseEfCoreStorage(builder =>
                        builder
                            .UseDbContext<ModuleTestOutboxDbContext>()
                            .EnforceTransactionalSetup()));
                })
                .BuildServiceProvider();

        act.Should().Throw<LiteBusConfigurationException>()
            .WithMessage("*EnforceTransactionalSetup*EnableSaveChangesInterceptor*AddLiteBusOutboxInterceptor*");
    }

    /// <summary>
    ///     Minimal outbox database context for module registration tests.
    /// </summary>
    private sealed class ModuleTestOutboxDbContext : DbContext, IOutboxDbContext
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="ModuleTestOutboxDbContext" /> class.
        /// </summary>
        /// <param name="options">The context options.</param>
        public ModuleTestOutboxDbContext(DbContextOptions<ModuleTestOutboxDbContext> options)
            : base(options)
        {
        }

        /// <inheritdoc />
        public DbSet<OutboxMessageEntity> OutboxMessages => Set<OutboxMessageEntity>();

        /// <inheritdoc />
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.GetModelBuilderConfiguration();
        }
    }
}
