using LiteBus.Commands.Storage.Marten;
using Npgsql;

namespace LiteBus.Storage.IntegrationTests.PostgreSql.Marten;

/// <summary>
///     Verifies which session failures the Marten idempotency store treats as a lost claim race. These tests need no
///     database.
/// </summary>
public sealed class MartenClaimConflictTests
{
    [Theory]
    [InlineData("23505")]
    [InlineData("40001")]
    [InlineData("40P01")]
    public void Conflict_state_in_the_inner_chain_is_a_claim_conflict(string sqlState)
    {
        var wrapped = new InvalidOperationException("the session failed", Postgres(sqlState));

        MartenIdempotencyStore.IsClaimConflict(wrapped).Should().BeTrue();
    }

    [Fact]
    public void Conflict_after_the_first_failure_of_an_aggregate_is_a_claim_conflict()
    {
        // A batched commit reports every failure in one AggregateException, and its InnerException is only the first.
        var aggregate = new AggregateException(
            new InvalidOperationException("an unrelated failure in the same batch"),
            new InvalidOperationException("the session failed", Postgres("40001")));

        MartenIdempotencyStore.IsClaimConflict(aggregate).Should().BeTrue();
    }

    [Fact]
    public void Other_storage_errors_are_not_claim_conflicts()
    {
        var aggregate = new AggregateException(
            Postgres("23503"),
            new InvalidOperationException("the session failed", Postgres("42P01")));

        MartenIdempotencyStore.IsClaimConflict(aggregate).Should().BeFalse();
    }

    private static PostgresException Postgres(string sqlState)
    {
        return new PostgresException("a storage error", "ERROR", "ERROR", sqlState);
    }
}
