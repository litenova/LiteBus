using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Storage.EntityFrameworkCore;
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
            .AddLiteBus(modules => modules.AddInboxModule(inbox => inbox.UseEfCoreStorage(builder =>
                builder.UseDbContext<ModuleTestInboxDbContext>())))
            .BuildServiceProvider();

        var store = provider.GetRequiredService<EfCoreInboxStore>();
        provider.GetRequiredService<IInboxStore>().Should().BeSameAs(store);
        provider.GetRequiredService<IInboxLeaseStore>().Should().BeSameAs(store);
        provider.GetRequiredService<IInboxTerminalStateStore>().Should().BeSameAs(store);
        provider.GetRequiredService<IInboxRetentionStore>().Should().BeSameAs(store);
        provider.GetRequiredService<IInboxDiagnosticsStore>().Should().BeSameAs(store);
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
