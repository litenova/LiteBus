using LiteBus.Messaging.Abstractions;
using Npgsql;

namespace LiteBus.Storage.UnitTests;

/// <summary>
///     Verifies that the PostgreSQL constraint classifier turns mapped unique and exclusion violations into denials.
/// </summary>
public sealed class PostgreSqlConstraintRefusalClassifierTests
{
    [Fact]
    public void Unique_violation_on_a_mapped_constraint_is_denied_with_the_mapped_code()
    {
        var classifier = new PostgreSqlConstraintRefusalClassifier(
            new Dictionary<string, string> { ["ux_orders_reference"] = "ORDER_REFERENCE_TAKEN" });

        var recognized = classifier.TryClassify(Conflict("ux_orders_reference"), out var refusal);

        recognized.Should().BeTrue();
        refusal.Outcome.Should().Be(MediationOutcome.Denied);
        refusal.Code.Should().Be("ORDER_REFERENCE_TAKEN");
        refusal.Reason.Should().Contain("ux_orders_reference");
    }

    [Fact]
    public void Exclusion_violation_on_a_mapped_constraint_is_denied()
    {
        var classifier = new PostgreSqlConstraintRefusalClassifier(
            new Dictionary<string, string> { ["ex_bookings_no_overlap"] = "BOOKING_OVERLAPS" });

        var recognized = classifier.TryClassify(Conflict("ex_bookings_no_overlap", "23P01"), out var refusal);

        recognized.Should().BeTrue();
        refusal.Outcome.Should().Be(MediationOutcome.Denied);
        refusal.Code.Should().Be("BOOKING_OVERLAPS");
    }

    [Fact]
    public void Wrapped_violation_is_found_anywhere_in_the_inner_chain()
    {
        var classifier = new PostgreSqlConstraintRefusalClassifier(
            new Dictionary<string, string> { ["ux_orders_reference"] = "ORDER_REFERENCE_TAKEN" });

        var wrapped = new InvalidOperationException(
            "the commit failed",
            new InvalidOperationException("the session failed", Conflict("ux_orders_reference")));

        var recognized = classifier.TryClassify(wrapped, out var refusal);

        recognized.Should().BeTrue();
        refusal.Code.Should().Be("ORDER_REFERENCE_TAKEN");
    }

    [Fact]
    public void Unmapped_constraint_passes_through()
    {
        var classifier = new PostgreSqlConstraintRefusalClassifier(
            new Dictionary<string, string> { ["ux_orders_reference"] = "ORDER_REFERENCE_TAKEN" });

        var recognized = classifier.TryClassify(Conflict("ux_orders_other"), out _);

        recognized.Should().BeFalse();
    }

    [Fact]
    public void Other_sql_state_passes_through_even_for_a_mapped_constraint()
    {
        var classifier = new PostgreSqlConstraintRefusalClassifier(
            new Dictionary<string, string> { ["ux_orders_reference"] = "ORDER_REFERENCE_TAKEN" });

        // 40001 is a serialization failure, which is retryable rather than a refusal.
        var recognized = classifier.TryClassify(Conflict("ux_orders_reference", "40001"), out _);

        recognized.Should().BeFalse();
    }

    [Fact]
    public void Missing_constraint_name_passes_through()
    {
        var classifier = new PostgreSqlConstraintRefusalClassifier(
            new Dictionary<string, string> { ["ux_orders_reference"] = "ORDER_REFERENCE_TAKEN" });

        var violation = new PostgresException(
            "duplicate key value violates unique constraint",
            "ERROR",
            "ERROR",
            "23505",
            constraintName: null);

        var recognized = classifier.TryClassify(violation, out _);

        recognized.Should().BeFalse();
    }

    [Fact]
    public void Non_postgres_fault_passes_through()
    {
        var classifier = new PostgreSqlConstraintRefusalClassifier(
            new Dictionary<string, string> { ["ux_orders_reference"] = "ORDER_REFERENCE_TAKEN" });

        var recognized = classifier.TryClassify(new InvalidOperationException("boom"), out _);

        recognized.Should().BeFalse();
    }

    [Fact]
    public void Options_construction_recognizes_the_same_map()
    {
        var classifier = new PostgreSqlConstraintRefusalClassifier(
            new PostgreSqlConstraintRefusalClassifier.Options(
                new Dictionary<string, string> { ["ux_orders_reference"] = "ORDER_REFERENCE_TAKEN" }));

        var recognized = classifier.TryClassify(Conflict("ux_orders_reference"), out var refusal);

        recognized.Should().BeTrue();
        refusal.Outcome.Should().Be(MediationOutcome.Denied);
        refusal.Code.Should().Be("ORDER_REFERENCE_TAKEN");
    }

    [Fact]
    public void Constraint_names_match_case_sensitively()
    {
        var classifier = new PostgreSqlConstraintRefusalClassifier(
            new Dictionary<string, string> { ["UX_ORDERS_REFERENCE"] = "ORDER_REFERENCE_TAKEN" });

        var recognized = classifier.TryClassify(Conflict("ux_orders_reference"), out _);

        recognized.Should().BeFalse();
    }

    [Fact]
    public void Null_inputs_are_rejected()
    {
        var classifier = new PostgreSqlConstraintRefusalClassifier(
            new Dictionary<string, string> { ["ux_orders_reference"] = "ORDER_REFERENCE_TAKEN" });

        var classify = () => classifier.TryClassify(null!, out _);
        classify.Should().Throw<ArgumentNullException>();

        IReadOnlyDictionary<string, string> missingMap = null!;
        var dictionary = () => new PostgreSqlConstraintRefusalClassifier(missingMap);
        dictionary.Should().Throw<ArgumentNullException>();

        PostgreSqlConstraintRefusalClassifier.Options missingOptions = null!;
        var options = () => new PostgreSqlConstraintRefusalClassifier(missingOptions);
        options.Should().Throw<ArgumentNullException>();
    }

    private static PostgresException Conflict(string constraintName, string sqlState = "23505")
    {
        return new PostgresException(
            "duplicate key value violates unique constraint",
            "ERROR",
            "ERROR",
            sqlState,
            constraintName: constraintName);
    }
}
