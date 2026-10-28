using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace LiteBus.Storage.UnitTests.EntityFrameworkCore.Outbox;

/// <summary>
///     Verifies the outbox save-changes interceptor persists every staged envelope field.
/// </summary>
public sealed class OutboxInterceptorFieldMappingTests
{
    /// <summary>
    ///     Confirms flush maps handler targeting, lease generation, and error type onto the stored row.
    /// </summary>
    [Fact]
    public async Task SavingChangesAsync_ShouldPersistHandlerLeaseGenerationAndErrorType()
    {
        var interceptor = new LiteBusOutboxSaveChangesInterceptor();

        var options = new DbContextOptionsBuilder<MappedOutboxDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .AddLiteBusOutboxInterceptor(interceptor)
            .Options;

        var context = new MappedOutboxDbContext(options);
        await using (context.ConfigureAwait(false))
        {
            await context.Database.EnsureCreatedAsync().ConfigureAwait(false);

            interceptor.Enqueue(context, new OutboxEnvelope
            {
                Id = Guid.NewGuid(),
                ContractName = "orders.events.submitted",
                ContractVersion = 1,
                Payload = """{"orderId":"1"}""",
                Topic = "orders",
                Handler = "Tests.OrderSubmittedHandler",
                CreatedAt = new DateTimeOffset(2026, 6, 4, 10, 0, 0, TimeSpan.Zero),
                Status = OutboxStatus.Pending,
                AttemptCount = 0,
                LeaseGeneration = 7,
                ErrorType = "Tests.FakeError"
            });

            await context.SaveChangesAsync().ConfigureAwait(false);

            var stored = await context.OutboxMessages.SingleAsync().ConfigureAwait(false);
            stored.Handler.Should().Be("Tests.OrderSubmittedHandler");
            stored.LeaseGeneration.Should().Be(7);
            stored.ErrorType.Should().Be("Tests.FakeError");
        }
    }

    private sealed class MappedOutboxDbContext : DbContext, IOutboxDbContext
    {
        public MappedOutboxDbContext(DbContextOptions<MappedOutboxDbContext> options)
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
