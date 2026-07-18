using LiteBus.Commands.Abstractions;

namespace LiteBus.Storage.IntegrationTests.PostgreSql;

internal sealed class ProcessCrashCommandHandler : ICommandHandler<ProcessCrashCommand>
{
    private readonly ProcessCrashProbe _probe;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ProcessCrashCommandHandler" /> class.
    /// </summary>
    /// <param name="probe">The probe that records handler entry and holds the active dispatch.</param>
    public ProcessCrashCommandHandler(ProcessCrashProbe probe)
    {
        _probe = probe;
    }

    public Task HandleAsync(ProcessCrashCommand message, CancellationToken cancellationToken = default)
    {
        return _probe.RecordAsync(message);
    }
}
