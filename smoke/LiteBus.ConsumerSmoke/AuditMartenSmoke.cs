using LiteBus.Auditing;
using LiteBus.Auditing.Abstractions;
using LiteBus.Auditing.Storage.Marten;
using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Commands.Storage.Marten;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using Marten;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.ConsumerSmoke;

/// <summary>
///     Sends one audited command through the Marten audit store and verifies its hash chain segment.
/// </summary>
internal static class AuditMartenSmoke
{
    /// <summary>
    ///     The audit category the scenario command records under.
    /// </summary>
    internal const string Category = "smoke-audit-marten";

    /// <summary>
    ///     Runs the Marten audit scenario.
    /// </summary>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <returns>A task that completes when the scenario has passed.</returns>
    internal static async Task RunAsync(string connectionString)
    {
        var recorder = new SmokeRecorder();
        var services = new ServiceCollection();
        services.AddSingleton(recorder);
        services.AddMarten(options =>
        {
            options.Connection(connectionString);
            options.DatabaseSchemaName = "litebus_smoke_audit_marten";
        });
        services.AddLiteBus(liteBus =>
        {
            liteBus.AddMessaging(_ =>
            {
            });
            liteBus.AddCommands(commands =>
            {
                commands.Register<AuditMartenCommand>();
                commands.Register<AuditMartenCommandHandler>();
                commands.UseMartenUnitOfWork();
            });
            liteBus.Modules.AddAuditing(auditing => auditing
                .ForCommands()
                .UseMartenStore()
                .UseHashChain(_ =>
                {
                }));
        });

        await using var provider = services.BuildServiceProvider();
        var hosted = await AuditSmokeHost.StartHostAsync(provider).ConfigureAwait(false);

        try
        {
            await provider.GetRequiredService<ICommandMediator>().SendAsync(new AuditMartenCommand()).ConfigureAwait(false);
            Smoke.Expect(recorder.Entries.Count == 1, "the audited Marten command handler ran once");

            var segment = $"{Category}/{DateTimeOffset.UtcNow.Year}";
            await AuditSmokeHost.WaitForLinkedRecordAsync<MartenAuditRecord>(provider, segment).ConfigureAwait(false);
            await hosted.StopAsync(CancellationToken.None).ConfigureAwait(false);

            var verification = await provider.GetRequiredService<IAuditChain>().VerifyAsync(segment).ConfigureAwait(false);
            Smoke.Expect(verification.IsIntact, "the Marten audit segment verifies intact");
            Smoke.Expect(verification.RecordCount == 1, "the Marten audit segment holds one record");
        }
        finally
        {
            await hosted.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }
}

/// <summary>
///     A command the Marten audit scenario sends to produce one audit record.
/// </summary>
[Audited("smoke.audit.record-marten", Category = AuditMartenSmoke.Category)]
internal sealed record AuditMartenCommand : ICommand;

/// <summary>
///     Records that the Marten audit scenario command ran.
/// </summary>
internal sealed class AuditMartenCommandHandler : ICommandHandler<AuditMartenCommand>
{
    /// <summary>
    ///     The recorder shared with the scenario.
    /// </summary>
    private readonly SmokeRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AuditMartenCommandHandler" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the scenario.</param>
    public AuditMartenCommandHandler(SmokeRecorder recorder)
    {
        ArgumentNullException.ThrowIfNull(recorder);
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task HandleAsync(AuditMartenCommand message, CancellationToken cancellationToken = default)
    {
        _recorder.Record("marten-audit-handled");
        return Task.CompletedTask;
    }
}
