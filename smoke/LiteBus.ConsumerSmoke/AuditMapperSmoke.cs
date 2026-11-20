using LiteBus.Auditing;
using LiteBus.Auditing.Abstractions;
using LiteBus.Auditing.Storage.Marten;
using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Commands.Storage.Marten;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using Marten;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.ConsumerSmoke;

/// <summary>
///     Proves that every audit write awaits the record mapper's asynchronous form in the mediation's scope, and that
///     a row the mapper writes through the store's session commits with the record on the success path and survives
///     the rollback on the out-of-band path.
/// </summary>
/// <remarks>
///     The mapper here does what an application with pseudonymous actors does: it takes a scoped service in its
///     constructor, creates a pseudonym row through the <see cref="IDocumentSession" /> the store offers, and stamps the
///     pseudonym on the stored document.
/// </remarks>
internal static class AuditMapperSmoke
{
    /// <summary>
    ///     The audit category of the command the scenario allows.
    /// </summary>
    internal const string AllowedCategory = "smoke-audit-mapper-allowed";

    /// <summary>
    ///     The audit category of the command the scenario denies.
    /// </summary>
    internal const string DeniedCategory = "smoke-audit-mapper-denied";

    /// <summary>
    ///     Runs the scenario against the Marten audit store.
    /// </summary>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <returns>A task that completes when the scenario has passed.</returns>
    internal static async Task RunAsync(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddScoped<SmokeMappingScopeMarker>();
        services.AddMarten(options =>
        {
            options.Connection(connectionString);
            options.DatabaseSchemaName = "litebus_smoke_audit_mapper";
        });
        services.AddLiteBus(liteBus =>
        {
            liteBus.AddMessaging(_ =>
            {
            });
            liteBus.AddCommands(commands =>
            {
                commands.Register<AuditMapperAllowedCommand>();
                commands.Register<AuditMapperDeniedCommand>();
                commands.Register<AuditMapperAllowedCommandHandler>();
                commands.Register<AuditMapperDeniedCommandHandler>();
                commands.Register<AuditMapperDeniedCommandGuard>();
                commands.UseMartenUnitOfWork();
            });
            liteBus.Modules.AddAuditing(auditing => auditing
                .ForCommands()
                .UseMartenStore()
                .UseHashChain(chain => chain.StoreAs<MartenAuditRecord, PseudonymizingAuditMapper>()));
        });

        await using var provider = services.BuildServiceProvider();
        var hosted = await AuditSmokeHost.StartHostAsync(provider).ConfigureAwait(false);

        try
        {
            var mediator = provider.GetRequiredService<ICommandMediator>();
            await mediator.SendAsync(new AuditMapperAllowedCommand()).ConfigureAwait(false);

            var denied = false;

            try
            {
                await mediator.SendAsync(new AuditMapperDeniedCommand()).ConfigureAwait(false);
            }
            catch (LiteBusMessageDeniedException)
            {
                denied = true;
            }

            Smoke.Expect(denied, "the guard denied the second command");

            var year = DateTimeOffset.UtcNow.Year;
            var allowedSegment = $"{AllowedCategory}/{year}";
            var deniedSegment = $"{DeniedCategory}/{year}";

            await AuditSmokeHost.WaitForLinkedRecordAsync<MartenAuditRecord>(provider, allowedSegment).ConfigureAwait(false);
            await AuditSmokeHost.WaitForLinkedRecordAsync<MartenAuditRecord>(provider, deniedSegment).ConfigureAwait(false);
            await hosted.StopAsync(CancellationToken.None).ConfigureAwait(false);

            var store = provider.GetRequiredService<IDocumentStore>();
            var session = store.QuerySession();

            await using (session.ConfigureAwait(false))
            {
                var pseudonyms = await session.Query<SmokeAuditPseudonym>().ToListAsync().ConfigureAwait(false);
                var records = await session.Query<MartenAuditRecord>()
                    .Where(record => record.Category == AllowedCategory || record.Category == DeniedCategory)
                    .ToListAsync()
                    .ConfigureAwait(false);

                Smoke.Expect(
                    pseudonyms.Any(pseudonym => pseudonym.Action == "smoke.audit.mapper-allowed"),
                    "the mapper's pseudonym row committed with the allowed command's record");
                Smoke.Expect(
                    pseudonyms.Any(pseudonym => pseudonym.Action == "smoke.audit.mapper-denied"),
                    "the mapper's pseudonym row survived the denied command's rollback with its out-of-band record");
                Smoke.Expect(records.Count == 2, $"two audit records were stored, found {records.Count}");
                Smoke.Expect(
                    records.All(record => record.Properties.TryGetValue(PseudonymizingAuditMapper.PseudonymProperty, out var value)
                                          && pseudonyms.Any(pseudonym => pseudonym.Id.ToString() == value)),
                    "every stored record carries the pseudonym the mapper created");
                Smoke.Expect(
                    records.All(record => record.Properties.TryGetValue(PseudonymizingAuditMapper.ScopeProperty, out var marker)
                                          && marker == "scoped"),
                    "the mapper resolved its constructor dependency from the mediation scope");
            }

            var chain = provider.GetRequiredService<IAuditChain>();
            var allowed = await chain.VerifyAsync(allowedSegment).ConfigureAwait(false);
            var deniedVerification = await chain.VerifyAsync(deniedSegment).ConfigureAwait(false);
            Smoke.Expect(allowed.IsIntact, "the allowed command's segment verifies intact");
            Smoke.Expect(deniedVerification.IsIntact, "the denied command's segment verifies intact");
        }
        finally
        {
            await hosted.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }
}

/// <summary>
///     A scoped service the mapper takes in its constructor, proving the mapper is resolved from the mediation scope.
/// </summary>
internal sealed class SmokeMappingScopeMarker
{
    /// <summary>
    ///     Gets the value the mapper stamps on the record.
    /// </summary>
    public string Value => "scoped";
}

/// <summary>
///     A pseudonym row the mapper creates in the store's session.
/// </summary>
internal sealed class SmokeAuditPseudonym
{
    /// <summary>
    ///     Gets or sets the pseudonym identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    ///     Gets or sets the audit action the pseudonym was created for.
    /// </summary>
    public string Action { get; set; } = string.Empty;
}

/// <summary>
///     Maps an audit record to the Marten document and creates a pseudonym row through the store's session.
/// </summary>
internal sealed class PseudonymizingAuditMapper : IAuditRecordMapper<MartenAuditRecord>
{
    /// <summary>
    ///     The document property holding the pseudonym identifier.
    /// </summary>
    internal const string PseudonymProperty = "smoke.pseudonym";

    /// <summary>
    ///     The document property holding the value of the scoped service.
    /// </summary>
    internal const string ScopeProperty = "smoke.scope";

    /// <summary>
    ///     The store's default mapper the record is mapped with first.
    /// </summary>
    private readonly MartenAuditRecordMapper _inner = new();

    /// <summary>
    ///     The scoped service resolved from the mediation scope.
    /// </summary>
    private readonly SmokeMappingScopeMarker _marker;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PseudonymizingAuditMapper" /> class.
    /// </summary>
    /// <param name="marker">The scoped service.</param>
    public PseudonymizingAuditMapper(SmokeMappingScopeMarker marker)
    {
        ArgumentNullException.ThrowIfNull(marker);
        _marker = marker;
    }

    /// <inheritdoc />
    public MartenAuditRecord Map(AuditRecord record)
    {
        throw new InvalidOperationException("The audit store must call MapAsync, which this smoke scenario proves.");
    }

    /// <inheritdoc />
    public ValueTask<MartenAuditRecord> MapAsync(
        AuditRecord record,
        AuditMappingContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(context);

        var pseudonym = new SmokeAuditPseudonym { Id = Guid.NewGuid(), Action = record.Action };
        context.Features.Get<IDocumentSession>().Store(pseudonym);

        var document = _inner.Map(record);
        document.Properties[PseudonymProperty] = pseudonym.Id.ToString();
        document.Properties[ScopeProperty] = _marker.Value;

        return ValueTask.FromResult(document);
    }
}

/// <summary>
///     A command the scenario allows.
/// </summary>
[Audited("smoke.audit.mapper-allowed", Category = AuditMapperSmoke.AllowedCategory)]
internal sealed record AuditMapperAllowedCommand : ICommand;

/// <summary>
///     A command the scenario's guard denies.
/// </summary>
[Audited("smoke.audit.mapper-denied", Category = AuditMapperSmoke.DeniedCategory)]
internal sealed record AuditMapperDeniedCommand : ICommand;

/// <summary>
///     Handles the allowed command.
/// </summary>
internal sealed class AuditMapperAllowedCommandHandler : ICommandHandler<AuditMapperAllowedCommand>
{
    /// <inheritdoc />
    public Task HandleAsync(AuditMapperAllowedCommand message, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

/// <summary>
///     Handles the denied command, which the guard stops before it runs.
/// </summary>
internal sealed class AuditMapperDeniedCommandHandler : ICommandHandler<AuditMapperDeniedCommand>
{
    /// <inheritdoc />
    public Task HandleAsync(AuditMapperDeniedCommand message, CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException("The guard denies this command, so its handler never runs.");
    }
}

/// <summary>
///     Denies every <see cref="AuditMapperDeniedCommand" />.
/// </summary>
internal sealed class AuditMapperDeniedCommandGuard : ICommandGuard<AuditMapperDeniedCommand>
{
    /// <inheritdoc />
    public Task<Verdict> DecideAsync(AuditMapperDeniedCommand message, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Verdict.Deny("The smoke scenario denies this command."));
    }
}
