namespace LiteBus.Storage.IntegrationTests.PostgreSql;

internal sealed class ProcessCrashProbe
{
    private static readonly TimeSpan OrphanedWorkerTimeout = TimeSpan.FromSeconds(30);
    private readonly string? _markerPath;

    internal ProcessCrashProbe(string? markerPath)
    {
        _markerPath = markerPath;
    }

    internal async Task RecordAsync(ProcessCrashCommand command)
    {
        if (string.IsNullOrWhiteSpace(_markerPath))
        {
            return;
        }

        await File.WriteAllTextAsync(_markerPath, command.WorkId.ToString("D"), CancellationToken.None).ConfigureAwait(false);
        await Task.Delay(OrphanedWorkerTimeout, CancellationToken.None).ConfigureAwait(false);
    }
}
