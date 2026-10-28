using System.Reflection;
using LiteBus.Storage.PostgreSql.Stores;
using Npgsql;

namespace LiteBus.Storage.UnitTests.PostgreSql;

/// <summary>
///     Covers that disposing the PostgreSQL work signal completes even when the listener loop faulted.
/// </summary>
public sealed class PostgreSqlWorkSignalDisposeTests
{
    [Fact]
    public async Task DisposeAsync_WhenListenerLoopFaulted_ShouldStillDispose()
    {
        var dataSource = NpgsqlDataSource.Create("Host=localhost;Database=litebus_dispose_probe;Username=postgres");
        await using var configuredDataSource = dataSource.ConfigureAwait(false);

        var signal = new PostgreSqlWorkSignal(dataSource, "litebus_dispose_probe");

        // The listener loop's last-resort catch is filtered on cancellation, so an exception raised after
        // cancellation was requested faults the task; disposal must swallow that fault and still clean up.
        var loopCts = new CancellationTokenSource();
        await loopCts.CancelAsync().ConfigureAwait(false);
        SetPrivateField(signal, "_listenerLoopCts", loopCts);
        SetPrivateField(
            signal,
            "_listenerLoopTask",
            Task.FromException(new InvalidOperationException("The listener loop faulted during shutdown.")));

        var act = () => signal.DisposeAsync().AsTask();

        await act.Should().NotThrowAsync().ConfigureAwait(false);
    }

    [Fact]
    public async Task DisposeAsync_WhenListenerNeverStarted_ShouldComplete()
    {
        var dataSource = NpgsqlDataSource.Create("Host=localhost;Database=litebus_dispose_probe;Username=postgres");
        await using var configuredDataSource = dataSource.ConfigureAwait(false);

        var signal = new PostgreSqlWorkSignal(dataSource, "litebus_dispose_probe");

        var act = () => signal.DisposeAsync().AsTask();

        await act.Should().NotThrowAsync().ConfigureAwait(false);
    }

    private static void SetPrivateField(object target, string fieldName, object? value)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        field!.SetValue(target, value);
    }
}
