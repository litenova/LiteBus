using System.Data.Common;
using LiteBus.Storage.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LiteBus.Storage.UnitTests.EntityFrameworkCore.Shared;

/// <summary>
///     Verifies that unique-constraint violations are recognized from the driver's structured error, not its message.
/// </summary>
public sealed class EfCoreUniqueViolationTests
{
    [Fact]
    public void PostgreSql_unique_violation_is_recognized_whatever_the_message_language()
    {
        var violation = Save(new PostgresException("doppelter Schlüsselwert verletzt Unique-Constraint", "FEHLER", "ERROR", "23505"));

        EfCoreUniqueViolation.IsUniqueViolation(violation).Should().BeTrue();
    }

    [Fact]
    public void PostgreSql_foreign_key_violation_is_not_a_unique_violation_even_when_the_message_says_duplicate()
    {
        var violation = Save(new PostgresException("duplicate parent missing", "ERROR", "ERROR", "23503"));

        EfCoreUniqueViolation.IsUniqueViolation(violation).Should().BeFalse();
    }

    [Theory]
    [InlineData(2627, true)]
    [InlineData(2601, true)]
    [InlineData(1062, true)]
    [InlineData(1586, true)]
    [InlineData(547, false)]
    [InlineData(1205, false)]
    public void Error_number_decides_for_SQL_Server_and_MySQL_drivers(int number, bool expected)
    {
        // The message carries a digit run that the old text match would have read as a unique violation.
        var violation = Save(new NumberedDriverException(number, "Violation for key 'order-1062-2627'"));

        EfCoreUniqueViolation.IsUniqueViolation(violation).Should().Be(expected);
    }

    [Theory]
    [InlineData(2067, true)]
    [InlineData(1555, true)]
    [InlineData(787, false)]
    public void Extended_result_code_decides_for_SQLite(int extendedCode, bool expected)
    {
        var violation = Save(new SqliteLikeDriverException(extendedCode, "UNIQUE constraint failed"));

        EfCoreUniqueViolation.IsUniqueViolation(violation).Should().Be(expected);
    }

    [Fact]
    public void Violation_inside_an_aggregate_is_recognized()
    {
        var violation = new AggregateException(
            new InvalidOperationException("an unrelated failure"),
            Save(new NumberedDriverException(2627, "Violation of PRIMARY KEY constraint")));

        EfCoreUniqueViolation.IsUniqueViolation(violation).Should().BeTrue();
    }

    [Fact]
    public void Message_text_decides_only_when_no_driver_error_carries_a_code()
    {
        EfCoreUniqueViolation.IsUniqueViolation(Save(new CodelessDriverException("Duplicate entry"))).Should().BeTrue();
        EfCoreUniqueViolation.IsUniqueViolation(Save(new CodelessDriverException("connection reset"))).Should().BeFalse();
    }

    private static DbUpdateException Save(Exception inner)
    {
        return new DbUpdateException("An error occurred while saving the entity changes.", inner);
    }

    /// <summary>
    ///     A driver exception with a public error number, as the SQL Server and MySQL drivers expose.
    /// </summary>
    private sealed class NumberedDriverException : DbException
    {
        public NumberedDriverException(int number, string message)
            : base(message)
        {
            Number = number;
        }

        public int Number { get; }
    }

    /// <summary>
    ///     A driver exception with a public extended result code, as the SQLite driver exposes.
    /// </summary>
    private sealed class SqliteLikeDriverException : DbException
    {
        public SqliteLikeDriverException(int extendedCode, string message)
            : base(message)
        {
            SqliteExtendedErrorCode = extendedCode;
        }

        public int SqliteExtendedErrorCode { get; }
    }

    /// <summary>
    ///     A driver exception that carries no structured code.
    /// </summary>
    private sealed class CodelessDriverException : DbException
    {
        public CodelessDriverException(string message)
            : base(message)
        {
        }
    }
}
