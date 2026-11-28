using System.Collections.Concurrent;
using LiteBus.Auditing;
using LiteBus.Auditing.Abstractions;
using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.Auditing;

/// <summary>
///     Verifies the identity and origin of the records a mediation produces: a time-ordered version 7 identifier, and
///     <see cref="AuditRecordOrigin.Mediation" />, so a mapper that keeps <see cref="AuditRecord.Id" /> gets both time order
///     and redelivery protection.
/// </summary>
public sealed class AuditRecordIdentityTests : LiteBusTestBase
{
    /// <summary>
    ///     Verifies a mediation record carries a version 7 identifier and the mediation origin.
    /// </summary>
    [Fact]
    public async Task A_mediation_record_has_a_version_7_id_and_the_mediation_origin()
    {
        var trail = new CapturingAuditTrail();
        using var provider = BuildProvider(trail);

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new IdentityTestCommand())
            .ConfigureAwait(false);

        var record = trail.Records.Should().ContainSingle().Subject;
        record.Id.Version.Should().Be(7);
        record.Origin.Should().Be(AuditRecordOrigin.Mediation);
    }

    /// <summary>
    ///     Verifies successive mediation records carry non-decreasing timestamps in their identifiers, so records sort by
    ///     identifier to the millisecond.
    /// </summary>
    [Fact]
    public async Task Successive_mediation_records_have_non_decreasing_id_timestamps()
    {
        var trail = new CapturingAuditTrail();
        using var provider = BuildProvider(trail);
        var mediator = provider.GetRequiredService<ICommandMediator>();

        for (var index = 0; index < 5; index++)
        {
            await mediator.SendAsync(new IdentityTestCommand()).ConfigureAwait(false);
        }

        // The first 48 bits of a version 7 identifier are its Unix millisecond timestamp: the first 13 characters of the
        // standard format. Identifiers made in one millisecond differ in their random bits, so only the prefix orders.
        var timestamps = trail.Records.Select(record => record.Id.ToString()[..13]).ToList();
        timestamps.Should().BeInAscendingOrder(StringComparer.Ordinal);
    }

    /// <summary>
    ///     Verifies a record built by application code defaults to the application origin.
    /// </summary>
    [Fact]
    public void A_record_built_by_application_code_has_the_application_origin()
    {
        var record = new AuditRecord
        {
            Action = "retention.segment-deleted",
            Outcome = AuditOutcome.Succeeded,
            OccurredAt = DateTimeOffset.UtcNow
        };

        record.Origin.Should().Be(AuditRecordOrigin.Application);
    }

    /// <summary>
    ///     Builds a container that audits commands into the given trail.
    /// </summary>
    /// <param name="trail">The trail that receives the records.</param>
    /// <returns>The built provider.</returns>
    private static ServiceProvider BuildProvider(IAuditTrail trail)
    {
        return new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddAuditing(auditing => auditing.UseTrailInstance(trail).ForCommands());
                registry.AddCommands(builder =>
                {
                    builder.Register<IdentityTestCommand>();
                    builder.Register<IdentityTestCommandHandler>();
                });
            })
            .BuildServiceProvider();
    }

    /// <summary>
    ///     A command whose audit position is declared by attribute.
    /// </summary>
    [Audited("tests.audit-record-identity", Category = "tests")]
    private sealed class IdentityTestCommand : ICommand;

    /// <summary>
    ///     Completes the command without doing any work.
    /// </summary>
    private sealed class IdentityTestCommandHandler : ICommandHandler<IdentityTestCommand>
    {
        /// <inheritdoc />
        public Task HandleAsync(IdentityTestCommand message, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>
    ///     An audit trail that keeps every record it receives, in order.
    /// </summary>
    private sealed class CapturingAuditTrail : IAuditTrail
    {
        /// <summary>
        ///     The records in the order they were written.
        /// </summary>
        private readonly ConcurrentQueue<AuditRecord> _records = new();

        /// <summary>
        ///     Gets the records in the order they were written.
        /// </summary>
        public IReadOnlyList<AuditRecord> Records => [.. _records];

        /// <inheritdoc />
        public Task WriteAsync(AuditRecord record, CancellationToken cancellationToken)
        {
            _records.Enqueue(record);
            return Task.CompletedTask;
        }
    }
}
