using LiteBus.Auditing.Abstractions;
using LiteBus.Messaging.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Auditing.UnitTests.Chain;

/// <summary>
///     Verifies the synchronous base class behind audit record mappers that need no services.
/// </summary>
public sealed class AuditRecordMapperTests
{
    /// <summary>
    ///     Maps an audit record to a fixed document and counts the calls.
    /// </summary>
    private sealed class TestAuditRecordMapper : AuditRecordMapper<string>
    {
        /// <summary>
        ///     Gets the number of times <see cref="Map" /> ran.
        /// </summary>
        public int MapCalls { get; private set; }

        /// <inheritdoc />
        public override string Map(AuditRecord record)
        {
            ArgumentNullException.ThrowIfNull(record);
            MapCalls++;
            return $"mapped:{record.Action}";
        }
    }

    [Fact]
    public async Task MapAsync_returns_the_map_result()
    {
        var mapper = new TestAuditRecordMapper();
        var record = new AuditRecord
        {
            Id = Guid.NewGuid(),
            Action = "orders.place",
            Outcome = AuditOutcome.Succeeded,
            OccurredAt = new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero)
        };
        var context = CreateContext();

        var document = await mapper.MapAsync(record, context, CancellationToken.None).ConfigureAwait(false);

        document.Should().Be("mapped:orders.place");
        mapper.MapCalls.Should().Be(1);
    }

    [Fact]
    public void MapAsync_throws_for_a_null_context()
    {
        var mapper = new TestAuditRecordMapper();
        var record = new AuditRecord
        {
            Id = Guid.NewGuid(),
            Action = "orders.place",
            Outcome = AuditOutcome.Succeeded,
            OccurredAt = new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero)
        };

        var act = () => mapper.MapAsync(record, null!, CancellationToken.None);

        act.Should().Throw<ArgumentNullException>();
    }

    /// <summary>
    ///     Builds the mapping context the base class validates.
    /// </summary>
    /// <returns>The context.</returns>
    private static AuditMappingContext CreateContext()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var evaluation = new MessageEvaluationContext(
            new object(),
            typeof(object),
            ExecutionIdentity.Anonymous,
            services,
            TimeProvider.System,
            new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero));

        return new AuditMappingContext(evaluation);
    }
}
