using LiteBus.Auditing;
using LiteBus.Auditing.Abstractions;
using LiteBus.Auditing.Storage.EntityFrameworkCore;
using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Storage.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.ConsumerSmoke;

/// <summary>
///     Sends one audited command through the Entity Framework Core audit store and verifies its hash chain segment.
/// </summary>
internal static class AuditEntityFrameworkCoreSmoke
{
    /// <summary>
    ///     The audit category the scenario command records under.
    /// </summary>
    internal const string Category = "smoke-audit-efcore";

    /// <summary>
    ///     Runs the Entity Framework Core audit scenario.
    /// </summary>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <returns>A task that completes when the scenario has passed.</returns>
    internal static async Task RunAsync(string connectionString)
    {
        // EnsureCreated does nothing once the database holds any table, and the earlier scenarios share this
        // database, so the audit tables are created directly. The scenario expects a fresh database, as in CI.
        await using (var setup = AuditSmokeDbContext.Create(connectionString))
        {
            await setup.GetService<IRelationalDatabaseCreator>().CreateTablesAsync().ConfigureAwait(false);
        }

        var recorder = new SmokeRecorder();
        var services = new ServiceCollection();
        services.AddSingleton(recorder);
        services.AddDbContext<AuditSmokeDbContext>(options => options.UseNpgsql(connectionString));
        services.AddDbContextFactory<AuditSmokeDbContext>(options => options.UseNpgsql(connectionString));
        services.AddSingleton<IAuditDbContextFactory, EfCoreAuditDbContextFactory<AuditSmokeDbContext>>();
        services.AddScoped<IAuditDbContext>(provider => provider.GetRequiredService<AuditSmokeDbContext>());
        services.AddLiteBus(liteBus =>
        {
            liteBus.AddMessaging(_ =>
            {
            });
            liteBus.AddCommands(commands =>
            {
                commands.Register<AuditEntityFrameworkCoreCommand>();
                commands.Register<AuditEntityFrameworkCoreCommandHandler>();
                commands.UseUnitOfWork<AuditSmokeUnitOfWork>();
            });
            liteBus.Modules.AddAuditing(auditing => auditing
                .ForCommands()
                .UseEntityFrameworkCoreStore<AuditSmokeDbContext>()
                .UseHashChain(_ =>
                {
                }));
        });

        await using var provider = services.BuildServiceProvider(Smoke.DevelopmentValidation);
        var hosted = await AuditSmokeHost.StartHostAsync(provider).ConfigureAwait(false);

        try
        {
            await provider.GetRequiredService<ICommandMediator>().SendAsync(new AuditEntityFrameworkCoreCommand()).ConfigureAwait(false);
            Smoke.Expect(recorder.Entries.Count == 1, "the audited Entity Framework Core command handler ran once");

            var segment = $"{Category}/{DateTimeOffset.UtcNow.Year}";
            await AuditSmokeHost.WaitForLinkedRecordAsync<EfCoreAuditRecord>(provider, segment).ConfigureAwait(false);
            await hosted.StopAsync(CancellationToken.None).ConfigureAwait(false);

            var verification = await provider.GetRequiredService<IAuditChain>().VerifyAsync(segment).ConfigureAwait(false);
            Smoke.Expect(verification.IsIntact, "the Entity Framework Core audit segment verifies intact");
            Smoke.Expect(verification.RecordCount == 1, "the Entity Framework Core audit segment holds one record");
        }
        finally
        {
            await hosted.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }
}

/// <summary>
///     A command the Entity Framework Core audit scenario sends to produce one audit record.
/// </summary>
[Audited("smoke.audit.record-efcore", Category = AuditEntityFrameworkCoreSmoke.Category)]
internal sealed record AuditEntityFrameworkCoreCommand : ICommand;

/// <summary>
///     Records that the Entity Framework Core audit scenario command ran.
/// </summary>
internal sealed class AuditEntityFrameworkCoreCommandHandler : ICommandHandler<AuditEntityFrameworkCoreCommand>
{
    /// <summary>
    ///     The recorder shared with the scenario.
    /// </summary>
    private readonly SmokeRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AuditEntityFrameworkCoreCommandHandler" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the scenario.</param>
    public AuditEntityFrameworkCoreCommandHandler(SmokeRecorder recorder)
    {
        ArgumentNullException.ThrowIfNull(recorder);
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task HandleAsync(AuditEntityFrameworkCoreCommand message, CancellationToken cancellationToken = default)
    {
        _recorder.Record("efcore-audit-handled");
        return Task.CompletedTask;
    }
}

/// <summary>
///     The application database context carrying the audit tables for the Entity Framework Core audit scenario.
/// </summary>
internal sealed class AuditSmokeDbContext : DbContext, IAuditDbContext
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="AuditSmokeDbContext" /> class.
    /// </summary>
    /// <param name="options">The context options.</param>
    public AuditSmokeDbContext(DbContextOptions<AuditSmokeDbContext> options)
        : base(options)
    {
    }

    /// <summary>
    ///     Gets the heads of the audit chain segments.
    /// </summary>
    public DbSet<AuditSegmentHeadEntity> AuditSegmentHeads => Set<AuditSegmentHeadEntity>();

    /// <summary>
    ///     Gets the stored audit checkpoints.
    /// </summary>
    public DbSet<AuditCheckpointEntity> AuditCheckpoints => Set<AuditCheckpointEntity>();

    /// <summary>
    ///     Creates a context on the smoke database.
    /// </summary>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <returns>A new context.</returns>
    internal static AuditSmokeDbContext Create(string connectionString)
    {
        return new AuditSmokeDbContext(new DbContextOptionsBuilder<AuditSmokeDbContext>().UseNpgsql(connectionString).Options);
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.GetModelBuilderConfiguration(
            new EntityFrameworkCoreAuditStoreOptions { SchemaName = "litebus_smoke_audit_ef" },
            EfCoreStorageProvider.PostgreSql);
    }
}

/// <summary>
///     Commits the scoped audit context as the mediation unit of work.
/// </summary>
internal sealed class AuditSmokeUnitOfWork : IUnitOfWork
{
    /// <summary>
    ///     The scoped context the audit trail staged its record on.
    /// </summary>
    private readonly AuditSmokeDbContext _context;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AuditSmokeUnitOfWork" /> class.
    /// </summary>
    /// <param name="context">The scoped context the audit trail staged its record on.</param>
    public AuditSmokeUnitOfWork(AuditSmokeDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <inheritdoc />
    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
