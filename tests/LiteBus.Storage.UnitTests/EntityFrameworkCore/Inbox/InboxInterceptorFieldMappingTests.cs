using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Storage.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace LiteBus.Storage.UnitTests.EntityFrameworkCore.Inbox;

/// <summary>
///     Verifies the inbox save-changes interceptor persists every staged envelope field.
/// </summary>
public sealed class InboxInterceptorFieldMappingTests
{
    /// <summary>
    ///     Confirms flush maps lease generation and error type onto the stored row.
    /// </summary>
    [Fact]
    public async Task SavingChangesAsync_ShouldPersistLeaseGenerationAndErrorType()
    {
        var interceptor = new LiteBusInboxSaveChangesInterceptor();

        var options = new DbContextOptionsBuilder<MappedInboxDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .AddLiteBusInboxInterceptor(interceptor)
            .Options;

        var context = new MappedInboxDbContext(options);
        await using (context.ConfigureAwait(false))
        {
            await context.Database.EnsureCreatedAsync().ConfigureAwait(false);

            interceptor.Enqueue(context, new InboxEnvelope
            {
                Id = Guid.NewGuid(),
                ContractName = "orders.place",
                ContractVersion = 1,
                Payload = """{"orderId":"1"}""",
                CreatedAt = new DateTimeOffset(2026, 6, 4, 10, 0, 0, TimeSpan.Zero),
                Status = InboxStatus.Pending,
                AttemptCount = 0,
                LeaseGeneration = 7,
                ErrorType = "Tests.FakeError"
            });

            await context.SaveChangesAsync().ConfigureAwait(false);

            var stored = await context.InboxMessages.SingleAsync().ConfigureAwait(false);
            stored.LeaseGeneration.Should().Be(7);
            stored.ErrorType.Should().Be("Tests.FakeError");
        }
    }

    private sealed class MappedInboxDbContext : DbContext, IInboxDbContext
    {
        public MappedInboxDbContext(DbContextOptions<MappedInboxDbContext> options)
            : base(options)
        {
        }

        public DbSet<InboxMessageEntity> InboxMessages => Set<InboxMessageEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.GetModelBuilderConfiguration();
        }
    }
}
