using System.Data.Common;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Mediator.UnitTests;

/// <summary>
///     Verifies storage-conflict classification of mediation and dispatch failures.
/// </summary>
public sealed class MediationExceptionFiltersConcurrencyTests
{
    /// <summary>
    ///     Verifies an optimistic-concurrency fault is recognized by its type-name suffix.
    /// </summary>
    [Fact]
    public void IsConcurrencyConflict_when_type_name_has_concurrency_suffix_should_return_true()
    {
        MediationExceptionFilters.IsConcurrencyConflict(new TestConcurrencyException()).Should().BeTrue();
    }

    /// <summary>
    ///     Verifies the Marten existence races are recognized without the concurrency suffix.
    /// </summary>
    [Fact]
    public void IsConcurrencyConflict_when_marten_existence_race_should_return_true()
    {
        MediationExceptionFilters.IsConcurrencyConflict(new ConcurrentUpdateException()).Should().BeTrue();
        MediationExceptionFilters.IsConcurrencyConflict(new DocumentAlreadyExistsException()).Should().BeTrue();
    }

    /// <summary>
    ///     Verifies a serialization failure or a deadlock SQLSTATE is recognized as a lost race.
    /// </summary>
    [Fact]
    public void IsConcurrencyConflict_when_sql_state_reports_serialization_or_deadlock_should_return_true()
    {
        MediationExceptionFilters.IsConcurrencyConflict(new StubSqlStateException("40001")).Should().BeTrue();
        MediationExceptionFilters.IsConcurrencyConflict(new StubSqlStateException("40P01")).Should().BeTrue();
    }

    /// <summary>
    ///     Verifies a deadlock or lock-wait error number is recognized as a lost race.
    /// </summary>
    [Fact]
    public void IsConcurrencyConflict_when_error_number_reports_deadlock_should_return_true()
    {
        MediationExceptionFilters.IsConcurrencyConflict(new StubNumberException(1205)).Should().BeTrue();
        MediationExceptionFilters.IsConcurrencyConflict(new StubNumberException(1213)).Should().BeTrue();
    }

    /// <summary>
    ///     Verifies a SQLite busy or locked result code is recognized as a lost race.
    /// </summary>
    [Fact]
    public void IsConcurrencyConflict_when_sqlite_code_reports_contention_should_return_true()
    {
        MediationExceptionFilters.IsConcurrencyConflict(new StubSqliteException(5)).Should().BeTrue();
        MediationExceptionFilters.IsConcurrencyConflict(new StubSqliteException(6)).Should().BeTrue();
    }

    /// <summary>
    ///     Verifies a conflict hidden in the inner chain is recognized.
    /// </summary>
    [Fact]
    public void IsConcurrencyConflict_when_conflict_hides_in_inner_chain_should_return_true()
    {
        var wrapped = new InvalidOperationException("dispatch failed", new TestConcurrencyException());

        MediationExceptionFilters.IsConcurrencyConflict(wrapped).Should().BeTrue();
    }

    /// <summary>
    ///     Verifies a conflict hidden in an aggregate is recognized even when it is not the first failure.
    /// </summary>
    [Fact]
    public void IsConcurrencyConflict_when_conflict_hides_in_aggregate_should_return_true()
    {
        var aggregate = new AggregateException(
            new InvalidOperationException("first failure"),
            new TestConcurrencyException());

        MediationExceptionFilters.IsConcurrencyConflict(aggregate).Should().BeTrue();
    }

    /// <summary>
    ///     Verifies a refusal that keeps a conflict as its cause still shows the conflict.
    /// </summary>
    [Fact]
    public void IsConcurrencyConflict_when_refusal_keeps_conflict_cause_should_return_true()
    {
        var refusal = new LiteBusMessageDeniedException(
            typeof(object),
            "conflicted",
            "TEST",
            new TestConcurrencyException());

        MediationExceptionFilters.IsConcurrencyConflict(refusal).Should().BeTrue();
    }

    /// <summary>
    ///     Verifies a unique violation is deterministic state rather than a lost race.
    /// </summary>
    [Fact]
    public void IsConcurrencyConflict_when_unique_violation_should_return_false()
    {
        MediationExceptionFilters.IsConcurrencyConflict(new StubSqlStateException("23505")).Should().BeFalse();
        MediationExceptionFilters.IsConcurrencyConflict(new StubNumberException(2627)).Should().BeFalse();
    }

    /// <summary>
    ///     Verifies an unrelated driver code is not mistaken for a lost race.
    /// </summary>
    [Fact]
    public void IsConcurrencyConflict_when_driver_code_is_unrelated_should_return_false()
    {
        MediationExceptionFilters.IsConcurrencyConflict(new StubSqlStateException("42000")).Should().BeFalse();
        MediationExceptionFilters.IsConcurrencyConflict(new StubNumberException(50000)).Should().BeFalse();
        MediationExceptionFilters.IsConcurrencyConflict(new StubSqliteException(1)).Should().BeFalse();
    }

    /// <summary>
    ///     Verifies a plain failure without storage content is not a conflict.
    /// </summary>
    [Fact]
    public void IsConcurrencyConflict_when_plain_failure_should_return_false()
    {
        MediationExceptionFilters.IsConcurrencyConflict(new InvalidOperationException("handler failed")).Should().BeFalse();
    }

    /// <summary>
    ///     Verifies a refusal without a conflict cause is not a conflict.
    /// </summary>
    [Fact]
    public void IsConcurrencyConflict_when_pure_refusal_should_return_false()
    {
        var refusal = new LiteBusMessageDeniedException(typeof(object), "denied", "TEST");

        MediationExceptionFilters.IsConcurrencyConflict(refusal).Should().BeFalse();
    }

    /// <summary>
    ///     Verifies a configuration fault without a conflict cause is not a conflict.
    /// </summary>
    [Fact]
    public void IsConcurrencyConflict_when_message_fault_should_return_false()
    {
        var fault = new MessageFaultException(typeof(object), "TEST", "misconfigured");

        MediationExceptionFilters.IsConcurrencyConflict(fault).Should().BeFalse();
    }

    /// <summary>
    ///     Verifies a null exception is rejected.
    /// </summary>
    [Fact]
    public void IsConcurrencyConflict_when_null_should_throw()
    {
        var act = () => MediationExceptionFilters.IsConcurrencyConflict(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    private sealed class TestConcurrencyException : Exception
    {
        public TestConcurrencyException()
            : base("lost race")
        {
        }
    }

    private sealed class ConcurrentUpdateException : Exception
    {
        public ConcurrentUpdateException()
            : base("concurrent update")
        {
        }
    }

    private sealed class DocumentAlreadyExistsException : Exception
    {
        public DocumentAlreadyExistsException()
            : base("document already exists")
        {
        }
    }

    private sealed class StubSqlStateException : DbException
    {
        public StubSqlStateException(string? sqlState)
        {
            SqlStateValue = sqlState;
        }

        private string? SqlStateValue { get; }

        public override string? SqlState => SqlStateValue;
    }

    private sealed class StubNumberException : DbException
    {
        public StubNumberException(int number)
        {
            Number = number;
        }

        public int Number { get; }
    }

    private sealed class StubSqliteException : DbException
    {
        public StubSqliteException(int sqliteErrorCode)
        {
            SqliteErrorCode = sqliteErrorCode;
        }

        public int SqliteErrorCode { get; }
    }
}
