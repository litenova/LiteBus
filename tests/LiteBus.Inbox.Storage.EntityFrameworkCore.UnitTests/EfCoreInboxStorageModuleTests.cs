using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Inbox.Abstractions;
using LiteBus.Messaging;
using LiteBus.Runtime.Abstractions.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Inbox.Storage.EntityFrameworkCore.UnitTests;

public sealed class EfCoreInboxStorageModuleTests
{
    [Fact]
    public void AddEfCoreInboxStorage_ShouldRegisterSingleStoreInstanceForAllRoles()
    {
        var databaseName = Guid.NewGuid().ToString("N");

        var provider = new ServiceCollection()
            .AddDbContext<ModuleTestInboxDbContext>(options => options.UseInMemoryDatabase(databaseName))
            .AddLiteBus(registry =>
            {
                registry.AddMessageModule(_ =>
                {
                });

                registry.AddInboxModule(inbox => inbox.UseEntityFrameworkCoreStorage(builder =>
                    builder.UseDbContext<ModuleTestInboxDbContext>()));
            })
            .BuildServiceProvider();

        var store = provider.GetRequiredService<EfCoreInboxStore>();
        provider.GetRequiredService<IInboxStore>().Should().BeSameAs(store);
        provider.GetRequiredService<IInboxLeaseStore>().Should().BeSameAs(store);
        provider.GetRequiredService<IInboxStateWriter>().Should().BeSameAs(store);
        provider.GetRequiredService<IInboxDeadLetterStore>().Should().BeSameAs(store);
        provider.GetRequiredService<IInboxRetentionStore>().Should().BeSameAs(store);
        provider.GetRequiredService<IInboxDiagnosticsStore>().Should().BeSameAs(store);
        provider.GetRequiredService<ITransactionalInboxStore>().Should().BeSameAs(store);
    }

    [Fact]
    public void AddEfCoreInboxStorage_WithSaveChangesInterceptor_ShouldRegisterTransactionalInbox()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var interceptor = new LiteBusInboxSaveChangesInterceptor();

        var provider = new ServiceCollection()
            .AddDbContext<ModuleTestInboxDbContext>(options => options
                .UseInMemoryDatabase(databaseName)
                .AddLiteBusInboxInterceptor(interceptor))
            .AddLiteBus(registry =>
            {
                registry.AddMessageModule(_ =>
                {
                });

                registry.AddInboxModule(inbox => inbox.UseEntityFrameworkCoreStorage(builder =>
                    builder
                        .UseDbContext<ModuleTestInboxDbContext>()
                        .EnableSaveChangesInterceptor()));
            })
            .BuildServiceProvider();

        provider.GetRequiredService<LiteBusInboxSaveChangesInterceptor>().Should().NotBeNull();

        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITransactionalInbox<ModuleTestInboxDbContext>>().Should().NotBeNull();
    }

    [Fact]
    public void AddEfCoreInboxStorage_WithEnforceTransactionalSetupWithoutInterceptor_ShouldThrowOnBuild()
    {
        var act = () =>
            new ServiceCollection()
                .AddDbContext<ModuleTestInboxDbContext>(options => options.UseInMemoryDatabase(Guid.NewGuid().ToString("N")))
                .AddLiteBus(registry =>
                {
                    registry.AddMessageModule(_ =>
                    {
                    });

                    registry.AddInboxModule(inbox => inbox.UseEntityFrameworkCoreStorage(builder =>
                        builder
                            .UseDbContext<ModuleTestInboxDbContext>()
                            .EnforceTransactionalSetup()));
                })
                .BuildServiceProvider();

        act.Should().Throw<LiteBusConfigurationException>()
            .WithMessage("*EnforceTransactionalSetup*EnableSaveChangesInterceptor*AddLiteBusInboxInterceptor*");
    }

    /// <summary>
    ///     Minimal inbox database context for module registration tests.
    /// </summary>
    private sealed class ModuleTestInboxDbContext : DbContext, IInboxDbContext
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="ModuleTestInboxDbContext" /> class.
        /// </summary>
        /// <param name="options">The context options.</param>
        public ModuleTestInboxDbContext(DbContextOptions<ModuleTestInboxDbContext> options)
            : base(options)
        {
        }

        /// <inheritdoc />
        public DbSet<InboxMessageEntity> InboxMessages => Set<InboxMessageEntity>();

        /// <inheritdoc />
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.GetModelBuilderConfiguration();
        }
    }
}