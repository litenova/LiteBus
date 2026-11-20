using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using LiteBus.Auditing;
using LiteBus.Auditing.Abstractions;
using LiteBus.Auditing.Checkpoints;
using LiteBus.Auditing.Hashing;
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
///     Drives the audit chain operations an application runs by hand, against the Marten store: a record hashed with an
///     application-defined keyed scheme, segments assigned by a delegate, a labelled explicit checkpoint that reaches
///     the publisher with its label, an expiry that is safe to retry, and the checkpoint loop switched off for the host.
/// </summary>
internal static class AuditChainOperationsSmoke
{
    /// <summary>
    ///     The audit category of the scenario's command.
    /// </summary>
    internal const string Category = "smoke-audit-ops";

    /// <summary>
    ///     The name of the application-defined keyed scheme.
    /// </summary>
    private const string KeyedScheme = "smoke-hmac-sha256-v1";

    /// <summary>
    ///     Runs the scenario.
    /// </summary>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <returns>A task that completes when the scenario has passed.</returns>
    internal static async Task RunAsync(string connectionString)
    {
        var recorder = new SmokeCheckpointRecorder();
        var services = new ServiceCollection();
        services.AddSingleton(recorder);
        services.AddMarten(options =>
        {
            options.Connection(connectionString);
            options.DatabaseSchemaName = "litebus_smoke_audit_ops";
        });
        services.AddLiteBus(liteBus =>
        {
            liteBus.AddMessaging(_ =>
            {
            });
            liteBus.AddCommands(commands =>
            {
                commands.Register<AuditOperationsCommand>();
                commands.Register<AuditOperationsCommandHandler>();
                commands.UseMartenUnitOfWork();
            });
            liteBus.Modules.AddAuditing(auditing => auditing
                .ForCommands()
                .UseMartenStore()
                .ConfigureCheckpointPublishing(publishing => publishing.Enabled = false)
                .UseHashChain(chain => chain
                    .AddScheme(KeyedScheme, new HmacSha256AuditHasher(Encoding.UTF8.GetBytes("smoke-audit-key-0001")))
                    .UseScheme(KeyedScheme)
                    .SegmentBy<MartenAuditRecord>(record => $"{record.Category}/{record.TargetId}")
                    .CheckpointWhen(CheckpointPolicies.Never())
                    .PublishCheckpointsTo<SmokeRecordingCheckpointPublisher>()));
        });

        await using var provider = services.BuildServiceProvider();

        var model = provider.GetRequiredService<ICompositionModel>();
        using (var json = JsonDocument.Parse(model.ToJson()))
        {
            var auditing = json.RootElement.GetProperty("sections").GetProperty("auditing");
            Smoke.Expect(
                !auditing.GetProperty("checkpointPublishingEnabled").GetBoolean(),
                "the composition model shows the checkpoint loop switched off for this host");
            Smoke.Expect(auditing.GetProperty("scheme").GetString() == KeyedScheme, "the chain writes with the keyed scheme");
        }

        var hosted = await AuditSmokeHost.StartHostAsync(provider).ConfigureAwait(false);

        try
        {
            var target = Guid.NewGuid().ToString("N");
            await provider.GetRequiredService<ICommandMediator>()
                .SendAsync(new AuditOperationsCommand(target))
                .ConfigureAwait(false);

            var segment = $"{Category}/{target}";
            await AuditSmokeHost.WaitForLinkedRecordAsync<MartenAuditRecord>(provider, segment).ConfigureAwait(false);
            await hosted.StopAsync(CancellationToken.None).ConfigureAwait(false);

            var chain = provider.GetRequiredService<IAuditChain>();

            var verification = await chain.VerifyAsync(segment).ConfigureAwait(false);
            Smoke.Expect(verification.IsIntact, "the segment hashed with the keyed scheme verifies intact");

            var store = provider.GetRequiredService<IDocumentStore>();
            var session = store.QuerySession();

            await using (session.ConfigureAwait(false))
            {
                var record = await session.Query<MartenAuditRecord>()
                    .SingleAsync(document => document.Segment == segment)
                    .ConfigureAwait(false);
                Smoke.Expect(record.HashScheme == KeyedScheme, "the record stores the keyed scheme's name");
            }

            var cut = await chain.CutCheckpointAsync(segment, "manual").ConfigureAwait(false);
            Smoke.Expect(cut.Label == "manual", "the explicit cut stores its label");

            var final = await chain.ExpireSegmentAsync(segment).ConfigureAwait(false);
            var again = await chain.ExpireSegmentAsync(segment).ConfigureAwait(false);
            Smoke.Expect(again.Id == final.Id, "expiring the segment again returns the stored final checkpoint");
            Smoke.Expect(
                recorder.Published.Any(checkpoint => checkpoint.Id == cut.Id && checkpoint.Label == "manual"),
                "the labelled checkpoint reached the publisher with its label");
            Smoke.Expect(
                recorder.Published.Any(checkpoint => checkpoint.Id == final.Id),
                "the final checkpoint reached the publisher");
        }
        finally
        {
            await hosted.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }
}

/// <summary>
///     Holds the checkpoints the scenario's publisher received.
/// </summary>
internal sealed class SmokeCheckpointRecorder
{
    /// <summary>
    ///     The received checkpoints.
    /// </summary>
    private readonly ConcurrentQueue<AuditCheckpoint> _published = new();

    /// <summary>
    ///     Gets the received checkpoints in the order they arrived.
    /// </summary>
    public IReadOnlyList<AuditCheckpoint> Published => [.. _published];

    /// <summary>
    ///     Records a received checkpoint.
    /// </summary>
    /// <param name="checkpoint">The checkpoint.</param>
    public void Add(AuditCheckpoint checkpoint)
    {
        _published.Enqueue(checkpoint);
    }
}

/// <summary>
///     A checkpoint publisher that records what it receives.
/// </summary>
internal sealed class SmokeRecordingCheckpointPublisher : ICheckpointPublisher
{
    /// <summary>
    ///     The recorder shared with the scenario.
    /// </summary>
    private readonly SmokeCheckpointRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SmokeRecordingCheckpointPublisher" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the scenario.</param>
    public SmokeRecordingCheckpointPublisher(SmokeCheckpointRecorder recorder)
    {
        ArgumentNullException.ThrowIfNull(recorder);
        _recorder = recorder;
    }

    /// <inheritdoc />
    public ValueTask PublishAsync(AuditCheckpoint checkpoint, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);

        _recorder.Add(checkpoint);
        return ValueTask.CompletedTask;
    }
}

/// <summary>
///     A command the scenario audits, targeting one fresh segment per run.
/// </summary>
/// <param name="TargetId">The target, which the delegate segmenter puts in the segment name.</param>
[Audited("smoke.audit.chain-operations", Category = AuditChainOperationsSmoke.Category)]
internal sealed record AuditOperationsCommand(string TargetId) : ICommand;

/// <summary>
///     Handles the scenario's command and names its target for the record.
/// </summary>
internal sealed class AuditOperationsCommandHandler : ICommandHandler<AuditOperationsCommand>
{
    /// <summary>
    ///     The audit scope the handler names the target through.
    /// </summary>
    private readonly IAuditScope _scope;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AuditOperationsCommandHandler" /> class.
    /// </summary>
    /// <param name="scope">The audit scope.</param>
    public AuditOperationsCommandHandler(IAuditScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        _scope = scope;
    }

    /// <inheritdoc />
    public Task HandleAsync(AuditOperationsCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        _scope.WithTarget(message.TargetId);
        return Task.CompletedTask;
    }
}
