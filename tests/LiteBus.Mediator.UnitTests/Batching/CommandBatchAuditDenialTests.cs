using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Mediator.UnitTests.Auditing;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using LiteBus.Messaging.Abstractions.Auditing;

namespace LiteBus.Mediator.UnitTests.Batching;

/// <summary>
///     Verifies that a batch stopped by a guard denial writes the stopping command's own audit record beside the
///     rewritten records of the commands that passed.
/// </summary>
[Collection("Sequential")]
public sealed class CommandBatchAuditDenialTests
{
    /// <summary>
    ///     A denial of the second command writes two records: the first rewritten as rolled back, the second denied.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task TrySendManyAsync_WhenSecondCommandIsDenied_ShouldWriteTheDenialAndTheRolledBackRecord()
    {
        var trail = new RecordingAuditTrail();
        var services = new ServiceCollection();
        services.AddSingleton<IAuditTrail>(trail);

        var provider = services.AddLiteBus(registry =>
        {
            registry.AddMessaging(_ => { });
            registry.AddCommands(builder =>
            {
                builder.Register<ApproveRefundCommand>();
                builder.Register<ApproveRefundCommandGuard>();
                builder.Register<ApproveRefundCommandHandler>();
                builder.EnableAuditing();
            });
        }).BuildServiceProvider();

        await using (provider.ConfigureAwait(false))
        {
            var mediator = provider.GetRequiredService<ICommandBatchMediator>();

            var result = await mediator.TrySendManyAsync(
            [
                new ApproveRefundCommand(),
                new ApproveRefundCommand { ShouldDeny = true }
            ]).ConfigureAwait(false);

            result.Outcome.Should().Be(CommandBatchOutcome.RolledBack);
            result.FailedIndex.Should().Be(1);

            var records = trail.Records.ToArray();
            records.Should().HaveCount(2);
            records[0].Outcome.Should().Be(AuditOutcome.Failed);
            records[0].FailureCode.Should().Be("litebus.batch.rolled_back");
            records[1].Outcome.Should().Be(AuditOutcome.Denied);
            records.Select(record => record.Properties["litebus.batch.id"]).Distinct().Should().ContainSingle();
        }
    }
}
