namespace LiteBus.Storage.PostgreSql.UnitTests;

public sealed class PostgreSqlSchemaInspectorTests
{
    [Fact]
    public void InferVersionFromColumns_returns_zero_when_no_version_columns_exist()
    {
        var columns = new HashSet<string>(StringComparer.Ordinal) { "id", "payload" };

        var versionColumns = new List<IReadOnlyList<string>>
        {
            new[] { "trace_context" }
        };

        PostgreSqlSchemaInspector.InferVersionFromColumns(columns, versionColumns).Should().Be(0);
    }

    [Fact]
    public void InferVersionFromColumns_returns_highest_matching_version()
    {
        var columns = new HashSet<string>(StringComparer.Ordinal)
        {
            "id",
            "payload",
            "trace_context",
            "tenant_id"
        };

        var versionColumns = new List<IReadOnlyList<string>>
        {
            new[] { "id", "payload" },
            new[] { "trace_context" },
            new[] { "tenant_id" }
        };

        PostgreSqlSchemaInspector.InferVersionFromColumns(columns, versionColumns).Should().Be(3);
    }

    [Fact]
    public void InferVersionFromColumns_stops_at_first_missing_column_group()
    {
        var columns = new HashSet<string>(StringComparer.Ordinal)
        {
            "id",
            "payload",
            "trace_context"
        };

        var versionColumns = new List<IReadOnlyList<string>>
        {
            new[] { "id", "payload" },
            new[] { "trace_context" },
            new[] { "tenant_id" }
        };

        PostgreSqlSchemaInspector.InferVersionFromColumns(columns, versionColumns).Should().Be(2);
    }

    [Fact]
    public void ValidateRequiredColumns_reports_missing_columns()
    {
        var columns = new HashSet<string>(StringComparer.Ordinal) { "id", "payload" };
        var requiredColumns = new List<string> { "id", "payload", "trace_context" };

        PostgreSqlSchemaInspector.ValidateRequiredColumns(columns, requiredColumns, out var missingColumns);

        missingColumns.Should().ContainSingle().Which.Should().Be("trace_context");
    }

    [Fact]
    public void ValidateRequiredColumns_returns_empty_when_all_columns_exist()
    {
        var columns = new HashSet<string>(StringComparer.Ordinal) { "id", "payload", "trace_context" };
        var requiredColumns = new List<string> { "id", "payload" };

        PostgreSqlSchemaInspector.ValidateRequiredColumns(columns, requiredColumns, out var missingColumns);

        missingColumns.Should().BeEmpty();
    }
}