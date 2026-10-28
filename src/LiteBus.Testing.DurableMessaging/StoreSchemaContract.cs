using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Inbox.Abstractions;
using LiteBus.Outbox.Abstractions;

namespace LiteBus.Testing;

/// <summary>
///     Asserts that inbox or outbox store schema metadata matches a pinned expectation with a readable diff.
/// </summary>
/// <remarks>
///     <para>
///         The assertion can check exactly what <see cref="StoreSchemaInfo" /> exposes: the component name, the
///         expected and recorded schema versions, and the database schema and table names. It cannot check column
///         lists, indexes, constraints, or migration history, because the diagnostics store returns none of them;
///         a version mismatch is the signal that the persisted shape moved and deserves a closer look.
///     </para>
/// </remarks>
public static class StoreSchemaContract
{
    /// <summary>
    ///     Compares store schema metadata against an expectation and lists every difference.
    /// </summary>
    /// <param name="actual">The schema metadata reported by the diagnostics store.</param>
    /// <param name="expected">The pinned schema identity the test expects.</param>
    /// <returns>One human-readable entry per difference; an empty list when the metadata matches.</returns>
    public static IReadOnlyList<string> GetDifferences(StoreSchemaInfo actual, StoreSchemaExpectation expected)
    {
        ArgumentNullException.ThrowIfNull(actual);
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentException.ThrowIfNullOrWhiteSpace(expected.Component);

        var differences = new List<string>();

        if (!string.Equals(actual.Component, expected.Component, StringComparison.Ordinal))
        {
            differences.Add($"Component mismatch: expected '{expected.Component}' but found '{actual.Component}'.");
        }

        if (actual.ExpectedVersion != expected.ExpectedVersion)
        {
            differences.Add(
                $"Expected version mismatch: expected {expected.ExpectedVersion} but the store requires {actual.ExpectedVersion}.");
        }

        if (actual.RecordedVersion != expected.RecordedVersion)
        {
            differences.Add(
                $"Recorded version mismatch: expected {expected.RecordedVersion} but the store recorded {actual.RecordedVersion}.");
        }

        if (expected.SchemaName is not null &&
            !string.Equals(actual.SchemaName, expected.SchemaName, StringComparison.Ordinal))
        {
            differences.Add(
                $"Schema name mismatch: expected '{expected.SchemaName}' but found '{FormatName(actual.SchemaName)}'.");
        }

        if (expected.TableName is not null &&
            !string.Equals(actual.TableName, expected.TableName, StringComparison.Ordinal))
        {
            differences.Add(
                $"Table name mismatch: expected '{expected.TableName}' but found '{FormatName(actual.TableName)}'.");
        }

        return differences;
    }

    /// <summary>
    ///     Throws when store schema metadata differs from an expectation.
    /// </summary>
    /// <param name="actual">The schema metadata reported by the diagnostics store.</param>
    /// <param name="expected">The pinned schema identity the test expects.</param>
    /// <exception cref="InvalidOperationException">The metadata differs; the message lists every difference.</exception>
    public static void AssertCompatible(StoreSchemaInfo actual, StoreSchemaExpectation expected)
    {
        ArgumentNullException.ThrowIfNull(actual);
        ArgumentNullException.ThrowIfNull(expected);

        var differences = GetDifferences(actual, expected);

        if (differences.Count == 0)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Store schema '{expected.Component}' does not match the expected contract:{Environment.NewLine}" +
            string.Join(Environment.NewLine, differences.Select(difference => $"- {difference}")));
    }

    /// <summary>
    ///     Reads outbox schema metadata from a diagnostics store and throws when it differs from an expectation.
    /// </summary>
    /// <param name="store">The diagnostics store that reports schema metadata.</param>
    /// <param name="expected">The pinned schema identity the test expects.</param>
    /// <param name="cancellationToken">A token that cancels the lookup.</param>
    /// <returns>A task that represents the asynchronous assertion operation.</returns>
    /// <exception cref="InvalidOperationException">The metadata differs; the message lists every difference.</exception>
    public static async Task AssertCompatibleAsync(
        IOutboxDiagnosticsStore store,
        StoreSchemaExpectation expected,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(expected);

        var actual = await store.GetSchemaInfoAsync(cancellationToken).ConfigureAwait(false);
        AssertCompatible(actual, expected);
    }

    /// <summary>
    ///     Reads inbox schema metadata from a diagnostics store and throws when it differs from an expectation.
    /// </summary>
    /// <param name="store">The diagnostics store that reports schema metadata.</param>
    /// <param name="expected">The pinned schema identity the test expects.</param>
    /// <param name="cancellationToken">A token that cancels the lookup.</param>
    /// <returns>A task that represents the asynchronous assertion operation.</returns>
    /// <exception cref="InvalidOperationException">The metadata differs; the message lists every difference.</exception>
    public static async Task AssertCompatibleAsync(
        IInboxDiagnosticsStore store,
        StoreSchemaExpectation expected,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(expected);

        var actual = await store.GetSchemaInfoAsync(cancellationToken).ConfigureAwait(false);
        AssertCompatible(actual, expected);
    }

    /// <summary>
    ///     Formats an optional schema or table name for a difference entry.
    /// </summary>
    /// <param name="name">The reported name, if any.</param>
    /// <returns>The name, or a null marker when the store reported no name.</returns>
    private static string FormatName(string? name)
    {
        return name ?? "<null>";
    }
}
