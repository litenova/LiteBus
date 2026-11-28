using LiteBus.Auditing;
using LiteBus.Auditing.Abstractions;
using LiteBus.Auditing.Storage.PostgreSql;
using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.ConsumerSmoke;

/// <summary>
///     Sends one audited command through the PostgreSQL audit store and verifies its hash chain segment.
/// </summary>
internal static class AuditPostgreSqlSmoke
{
    /// <summary>
    ///     The audit category the scenario command records under.
    /// </summary>
    internal const string Category = "smoke-audit-postgresql";

    /// <summary>
    ///     Runs the PostgreSQL audit scenario.
    /// </summary>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <returns>A task that completes when the scenario has passed.</returns>
    internal static async Task RunAsync(string connectionString)
    {
        var recorder = new SmokeRecorder();
        var services = new ServiceCollection();
        services.AddSingleton(recorder);
        services.AddLiteBus(liteBus =>
        {
            liteBus.AddMessaging(_ =>
            {
            });
            liteBus.AddCommands(commands =>
            {
                commands.Register<AuditPostgreSqlCommand>();
                commands.Register<AuditPostgreSqlCommandHandler>();
            });
            liteBus.Modules.AddAuditing(auditing => auditing
                .ForCommands()
                .UsePostgreSqlStore(postgres => postgres
                    .UseConnectionString(connectionString)
                    .UseOptions(new PostgreSqlAuditStoreOptions { SchemaName = "litebus_smoke_audit_pg" }))
                .UseHashChain(_ =>
                {
                }));
        });

        await using var provider = services.BuildServiceProvider(Smoke.DevelopmentValidation);
        var hosted = await AuditSmokeHost.StartHostAsync(provider).ConfigureAwait(false);

        try
        {
            await provider.GetRequiredService<ICommandMediator>().SendAsync(new AuditPostgreSqlCommand()).ConfigureAwait(false);
            Smoke.Expect(recorder.Entries.Count == 1, "the audited PostgreSQL command handler ran once");

            var segment = $"{Category}/{DateTimeOffset.UtcNow.Year}";
            await AuditSmokeHost.WaitForLinkedRecordAsync<PostgreSqlAuditRecord>(provider, segment).ConfigureAwait(false);
            await hosted.StopAsync(CancellationToken.None).ConfigureAwait(false);

            var verification = await provider.GetRequiredService<IAuditChain>().VerifyAsync(segment).ConfigureAwait(false);
            Smoke.Expect(verification.IsIntact, "the PostgreSQL audit segment verifies intact");
            Smoke.Expect(verification.RecordCount == 1, "the PostgreSQL audit segment holds one record");
        }
        finally
        {
            await hosted.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }
}

/// <summary>
///     A command the PostgreSQL audit scenario sends to produce one audit record.
/// </summary>
[Audited("smoke.audit.record-postgresql", Category = AuditPostgreSqlSmoke.Category)]
internal sealed record AuditPostgreSqlCommand : ICommand;

/// <summary>
///     Records that the PostgreSQL audit scenario command ran.
/// </summary>
internal sealed class AuditPostgreSqlCommandHandler : ICommandHandler<AuditPostgreSqlCommand>
{
    /// <summary>
    ///     The recorder shared with the scenario.
    /// </summary>
    private readonly SmokeRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AuditPostgreSqlCommandHandler" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the scenario.</param>
    public AuditPostgreSqlCommandHandler(SmokeRecorder recorder)
    {
        ArgumentNullException.ThrowIfNull(recorder);
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task HandleAsync(AuditPostgreSqlCommand message, CancellationToken cancellationToken = default)
    {
        _recorder.Record("postgresql-audit-handled");
        return Task.CompletedTask;
    }
}
