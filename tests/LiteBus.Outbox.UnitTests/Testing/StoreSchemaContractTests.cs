using LiteBus.Runtime.Abstractions.Diagnostics;
using LiteBus.Testing;

namespace LiteBus.Outbox.UnitTests.Testing;

/// <summary>
///     Verifies schema compatibility diffs reported by <see cref="StoreSchemaContract" />.
/// </summary>
[Collection("Sequential")]
public sealed class StoreSchemaContractTests : LiteBusTestBase
{
    /// <summary>
    ///     Verifies that matching metadata produces no differences.
    /// </summary>
    [Fact]
    public void GetDifferences_when_matching_should_return_empty()
    {
        var actual = new StoreSchemaInfo("outbox", 3, 3, "app", "outbox_messages");
        var expected = new StoreSchemaExpectation
        {
            Component = "outbox",
            ExpectedVersion = 3,
            RecordedVersion = 3,
            SchemaName = "app",
            TableName = "outbox_messages"
        };

        StoreSchemaContract.GetDifferences(actual, expected).Should().BeEmpty();
    }

    /// <summary>
    ///     Verifies that a component mismatch is reported by name.
    /// </summary>
    [Fact]
    public void GetDifferences_when_component_differs_should_report_component()
    {
        var actual = new StoreSchemaInfo("inbox", 1, 1);

        var differences = StoreSchemaContract.GetDifferences(actual, OutboxVersionOne());

        differences.Should().ContainSingle().Which.Should().Contain("Component mismatch");
    }

    /// <summary>
    ///     Verifies that an expected version mismatch is reported by name.
    /// </summary>
    [Fact]
    public void GetDifferences_when_expected_version_differs_should_report_expected_version()
    {
        var actual = new StoreSchemaInfo("outbox", 2, 1);

        var differences = StoreSchemaContract.GetDifferences(actual, OutboxVersionOne());

        differences.Should().ContainSingle().Which.Should().Contain("Expected version mismatch");
    }

    /// <summary>
    ///     Verifies that a recorded version mismatch is reported by name.
    /// </summary>
    [Fact]
    public void GetDifferences_when_recorded_version_differs_should_report_recorded_version()
    {
        var actual = new StoreSchemaInfo("outbox", 1, 2);

        var differences = StoreSchemaContract.GetDifferences(actual, OutboxVersionOne());

        differences.Should().ContainSingle().Which.Should().Contain("Recorded version mismatch");
    }

    /// <summary>
    ///     Verifies that a schema name mismatch is reported by name.
    /// </summary>
    [Fact]
    public void GetDifferences_when_schema_name_differs_should_report_schema_name()
    {
        var actual = new StoreSchemaInfo("outbox", 1, 1, "legacy", "outbox_messages");
        var expected = OutboxVersionOne() with { SchemaName = "app", TableName = "outbox_messages" };

        var differences = StoreSchemaContract.GetDifferences(actual, expected);

        differences.Should().ContainSingle().Which.Should().Contain("Schema name mismatch");
    }

    /// <summary>
    ///     Verifies that a table name mismatch is reported by name.
    /// </summary>
    [Fact]
    public void GetDifferences_when_table_name_differs_should_report_table_name()
    {
        var actual = new StoreSchemaInfo("outbox", 1, 1, "app", "outbox_legacy");
        var expected = OutboxVersionOne() with { SchemaName = "app", TableName = "outbox_messages" };

        var differences = StoreSchemaContract.GetDifferences(actual, expected);

        differences.Should().ContainSingle().Which.Should().Contain("Table name mismatch");
    }

    /// <summary>
    ///     Verifies that unpinned schema and table names skip the name checks.
    /// </summary>
    [Fact]
    public void GetDifferences_when_names_unpinned_should_skip_name_checks()
    {
        var actual = new StoreSchemaInfo("outbox", 1, 1, "app", "outbox_messages");

        StoreSchemaContract.GetDifferences(actual, OutboxVersionOne()).Should().BeEmpty();
    }

    /// <summary>
    ///     Verifies that a compatible schema does not throw.
    /// </summary>
    [Fact]
    public void AssertCompatible_when_matching_should_not_throw()
    {
        var actual = new StoreSchemaInfo("outbox", 1, 1);

        var act = () => StoreSchemaContract.AssertCompatible(actual, OutboxVersionOne());

        act.Should().NotThrow();
    }

    /// <summary>
    ///     Verifies that an incompatible schema throws once with every difference in the message.
    /// </summary>
    [Fact]
    public void AssertCompatible_when_different_should_throw_with_every_difference()
    {
        var actual = new StoreSchemaInfo("inbox", 2, 1, "legacy", "inbox_messages");
        var expected = new StoreSchemaExpectation
        {
            Component = "outbox",
            ExpectedVersion = 3,
            RecordedVersion = 3,
            SchemaName = "app",
            TableName = "outbox_messages"
        };

        var act = () => StoreSchemaContract.AssertCompatible(actual, expected);
        var message = act.Should().Throw<InvalidOperationException>().Which.Message;

        message.Should().Contain("Component mismatch");
        message.Should().Contain("Expected version mismatch");
        message.Should().Contain("Recorded version mismatch");
        message.Should().Contain("Schema name mismatch");
        message.Should().Contain("Table name mismatch");
    }

    /// <summary>
    ///     Verifies that the asynchronous assertion reads schema metadata from an outbox diagnostics store.
    /// </summary>
    [Fact]
    public async Task AssertCompatibleAsync_should_read_schema_from_outbox_diagnostics_store()
    {
        var store = new TestOutboxStore();

        await StoreSchemaContract.AssertCompatibleAsync(store, OutboxVersionOne()).ConfigureAwait(false);

        Func<Task> act = () => StoreSchemaContract.AssertCompatibleAsync(store, OutboxVersionOne() with
        {
            RecordedVersion = 2
        });

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Recorded version mismatch*").ConfigureAwait(false);
    }

    /// <summary>
    ///     Builds the pinned expectation for a version one outbox store without name pins.
    /// </summary>
    /// <returns>The version one outbox expectation.</returns>
    private static StoreSchemaExpectation OutboxVersionOne()
    {
        return new StoreSchemaExpectation
        {
            Component = "outbox",
            ExpectedVersion = 1,
            RecordedVersion = 1
        };
    }
}
