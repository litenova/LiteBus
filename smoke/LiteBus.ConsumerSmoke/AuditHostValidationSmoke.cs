using LiteBus.Auditing;
using LiteBus.Auditing.Abstractions;
using LiteBus.Auditing.Storage.Marten;
using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Commands.Storage.Marten;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Testing;
using Marten;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.ConsumerSmoke;

/// <summary>
///     Validates a Marten audit host's container the way a consumer's unit test would, then runs it under the
///     Development container validation with a daily checkpoint policy and verifies the new segment.
/// </summary>
/// <remarks>
///     The audit engine and its background loops are singletons that use the chain store, so a store that captured
///     the scoped Marten session failed this host at start. A segment that was never checkpointed under a daily policy
///     must verify intact on the day it opened, because no checkpoint is due yet.
/// </remarks>
internal static class AuditHostValidationSmoke
{
    /// <summary>
    ///     The audit category the scenario command records under.
    /// </summary>
    internal const string Category = "smoke-audit-host-validation";

    /// <summary>
    ///     Runs the host validation scenario.
    /// </summary>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <returns>A task that completes when the scenario has passed.</returns>
    internal static async Task RunAsync(string connectionString)
    {
        LiteBusHostValidation.Validate(Compose(connectionString));

        var services = Compose(connectionString);
        await using var provider = services.BuildServiceProvider(Smoke.DevelopmentValidation);
        var hosted = await AuditSmokeHost.StartHostAsync(provider).ConfigureAwait(false);

        try
        {
            await provider.GetRequiredService<ICommandMediator>()
                .SendAsync(new AuditHostValidationCommand())
                .ConfigureAwait(false);

            var segment = $"{Category}/{DateTimeOffset.UtcNow.Year}";
            await AuditSmokeHost.WaitForLinkedRecordAsync<MartenAuditRecord>(provider, segment).ConfigureAwait(false);

            var verification = await provider.GetRequiredService<IAuditChain>().VerifyAsync(segment).ConfigureAwait(false);
            Smoke.Expect(
                verification.IsIntact,
                "a segment opened today verifies intact under a daily checkpoint policy before its first checkpoint");
            Smoke.Expect(verification.IsChainIntact, "the chain of the new segment reports no integrity break");
        }
        finally
        {
            await hosted.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Composes the host's services: Marten, the command module with the Marten unit of work, and the Marten audit
    ///     store with the hash chain under a daily checkpoint policy.
    /// </summary>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <returns>The composed services.</returns>
    private static ServiceCollection Compose(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddMarten(options =>
        {
            options.Connection(connectionString);
            options.DatabaseSchemaName = "litebus_smoke_audit_host_validation";
        });
        services.AddLiteBus(liteBus =>
        {
            liteBus.AddMessaging(_ =>
            {
            });
            liteBus.AddCommands(commands =>
            {
                commands.Register<AuditHostValidationCommand>();
                commands.Register<AuditHostValidationCommandHandler>();
                commands.UseMartenUnitOfWork();
            });
            liteBus.Modules.AddAuditing(auditing => auditing
                .ForCommands()
                .UseMartenStore()
                .UseHashChain(chain => chain.CheckpointWhen(CheckpointPolicies.DailyAt(TimeOnly.MinValue))));
        });

        return services;
    }
}

/// <summary>
///     A command the host validation scenario sends to produce one audit record.
/// </summary>
[Audited("smoke.audit.host-validation", Category = AuditHostValidationSmoke.Category)]
internal sealed record AuditHostValidationCommand : ICommand;

/// <summary>
///     Handles the host validation scenario command; the audit record is the only effect the scenario checks.
/// </summary>
internal sealed class AuditHostValidationCommandHandler : ICommandHandler<AuditHostValidationCommand>
{
    /// <inheritdoc />
    public Task HandleAsync(AuditHostValidationCommand message, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
