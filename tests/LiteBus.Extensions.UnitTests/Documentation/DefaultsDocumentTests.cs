using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using LiteBus.Inbox.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox.Abstractions;

namespace LiteBus.Extensions.UnitTests.Documentation;

/// <summary>
///     Verifies the operations defaults page lists every options property with the default a new instance carries.
/// </summary>
public sealed class DefaultsDocumentTests
{
    /// <summary>
    ///     Gets the options types covered by the defaults page, one per documented table.
    /// </summary>
    /// <returns>The covered options types.</returns>
    public static TheoryData<Type> OptionTypes()
    {
        var data = new TheoryData<Type>();

        foreach (var type in CoveredTypes)
        {
            data.Add(type);
        }

        return data;
    }

    /// <summary>
    ///     Verifies the defaults page holds exactly one table per covered options type.
    /// </summary>
    [Fact]
    public void DefaultsDocument_ShouldContainOneTablePerOptionsType()
    {
        var tables = ParseDefaultsDocument();

        var expected = CoveredTypes.Select(type => type.Name).ToArray();
        tables.Select(table => table.TypeName).Should().BeEquivalentTo(expected);
    }

    /// <summary>
    ///     Verifies one options table names every property, documents the consequence of changing each one, and
    ///     matches the defaults a new options instance carries.
    /// </summary>
    /// <param name="optionsType">The options type whose table is verified.</param>
    [Theory]
    [MemberData(nameof(OptionTypes))]
    public void DefaultsDocument_ShouldMatchSourceDefaults(Type optionsType)
    {
        var tables = ParseDefaultsDocument();
        var table = tables.SingleOrDefault(candidate => candidate.TypeName == optionsType.Name);
        table.Should().NotBeNull($"the defaults page must hold a table for {optionsType.Name}.");

        var properties = optionsType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanRead && property.GetIndexParameters().Length == 0)
            .ToArray();

        table!.Rows.Keys.Should().BeEquivalentTo(
            properties.Select(property => property.Name),
            $"the {optionsType.Name} table must name every property and no other.");

        var instance = Activator.CreateInstance(optionsType);
        instance.Should().NotBeNull($"{optionsType.Name} must have a parameterless constructor.");

        foreach (var property in properties)
        {
            var row = table.Rows[property.Name];
            row.Effect.Should().NotBeNullOrWhiteSpace(
                $"the {optionsType.Name}.{property.Name} row must state the consequence of changing it.");

            var actual = FormatDefault(property.GetValue(instance));
            row.Default.Should().Be(
                actual,
                $"the documented {optionsType.Name}.{property.Name} default must match a new instance.");
        }
    }

    /// <summary>
    ///     Gets the options types covered by the defaults page.
    /// </summary>
    private static readonly Type[] CoveredTypes =
    [
        typeof(InboxProcessorOptions),
        typeof(OutboxProcessorOptions),
        typeof(InboxProcessorHostOptions),
        typeof(OutboxProcessorHostOptions),
        typeof(RetryOptions),
        typeof(OutboxImmediateDispatchOptions),
        typeof(InboxCleanupHostOptions),
        typeof(OutboxCleanupHostOptions),
    ];

    /// <summary>
    ///     Matches the first inline code span of a table cell, which carries the machine-readable default value.
    /// </summary>
    private static readonly Regex CodeSpanRegex = new("`([^`]+)`", RegexOptions.Compiled);

    /// <summary>
    ///     Reads the defaults page and parses one table per options section.
    /// </summary>
    /// <returns>The parsed tables in document order.</returns>
    private static List<DocumentedTable> ParseDefaultsDocument()
    {
        List<DocumentedTable> tables = [];
        DocumentedTable? current = null;

        foreach (var line in File.ReadAllLines(FindDefaultsDocument()))
        {
            var trimmed = line.Trim();

            if (trimmed.StartsWith("## `", StringComparison.Ordinal))
            {
                var typeName = CodeSpanRegex.Match(trimmed).Groups[1].Value;
                current = new DocumentedTable(typeName);
                tables.Add(current);
                continue;
            }

            if (current is null || !trimmed.StartsWith('|'))
            {
                continue;
            }

            var cells = trimmed.Split('|');

            if (cells.Length < 4 || cells[1].Contains("Property", StringComparison.Ordinal) ||
                cells[1].Contains("---", StringComparison.Ordinal))
            {
                continue;
            }

            var property = cells[1].Trim().Trim('`');
            var defaultMatch = CodeSpanRegex.Match(cells[2]);
            defaultMatch.Success.Should().BeTrue(
                $"the {current.TypeName}.{property} default cell must start with a code span.");
            current.Rows[property] = new DocumentedRow(property, defaultMatch.Groups[1].Value, cells[3].Trim());
        }

        return tables;
    }

    /// <summary>
    ///     Locates the defaults page by walking from the test output directory toward the repository root.
    /// </summary>
    /// <returns>The absolute path of the defaults page.</returns>
    private static string FindDefaultsDocument()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName, "site", "content", "docs", "operations", "defaults.md");

            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            "Could not locate site/content/docs/operations/defaults.md above " + AppContext.BaseDirectory + ".");
    }

    /// <summary>
    ///     Formats a default property value exactly as the machine-readable code span in the defaults page shows it.
    /// </summary>
    /// <param name="value">The property value read from a new options instance.</param>
    /// <returns>The documented rendering of the value.</returns>
    private static string FormatDefault(object? value)
    {
        return value switch
        {
            null => "null",
            bool flag => flag ? "true" : "false",
            int number => number.ToString(CultureInfo.InvariantCulture),
            TimeSpan duration => duration.ToString("c", CultureInfo.InvariantCulture),
            RetryOptions => "new RetryOptions()",
            IReadOnlyList<TimeSpan> delays => delays.Count == 0 ? "empty" : string.Join(",", delays),
            Enum enumeration => enumeration.ToString(),
            string text => "\"" + text + "\"",
            _ => value.ToString() ?? "null",
        };
    }

    /// <summary>
    ///     Holds the parsed rows of one options table from the defaults page.
    /// </summary>
    private sealed class DocumentedTable
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="DocumentedTable" /> class.
        /// </summary>
        /// <param name="typeName">The options type name taken from the section heading.</param>
        public DocumentedTable(string typeName)
        {
            ArgumentNullException.ThrowIfNull(typeName);
            TypeName = typeName;
            Rows = new Dictionary<string, DocumentedRow>(StringComparer.Ordinal);
        }

        /// <summary>
        ///     Gets the options type name taken from the section heading.
        /// </summary>
        public string TypeName { get; }

        /// <summary>
        ///     Gets the parsed rows keyed by property name.
        /// </summary>
        public Dictionary<string, DocumentedRow> Rows { get; }
    }

    /// <summary>
    ///     Holds one parsed property row from a defaults table.
    /// </summary>
    /// <param name="Property">The property name.</param>
    /// <param name="Default">The machine-readable default from the first code span of the default cell.</param>
    /// <param name="Effect">The documented consequence of changing the property.</param>
    private sealed record DocumentedRow(string Property, string Default, string Effect);
}
