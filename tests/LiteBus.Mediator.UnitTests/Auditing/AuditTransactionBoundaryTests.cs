using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.Auditing;

/// <summary>
///     Verifies that an application can make an audit record atomic with the change it describes by committing its unit
///     of work at <see cref="HandlerPriorities.UnitOfWork" />.
/// </summary>
/// <remarks>
///     This is the pattern the auditing documentation prescribes, and it only works because the completion stage orders
///     by priority alone and because <see cref="HandlerPriorities.UnitOfWork" /> sits above every value LiteBus
///     reserves. Without both, the commit could not be placed after the audit writer, and no database-backed
///     application could adopt the shipped writer.
/// </remarks>
[Collection("Sequential")]
public sealed class AuditTransactionBoundaryTests : LiteBusTestBase
{
    /// <summary>
    ///     Builds a provider whose trail stages records into a unit of work committed at the completion stage.
    /// </summary>
    /// <param name="unitOfWork">The unit of work shared by the trail and the commit handler.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildProvider(FakeUnitOfWork unitOfWork)
    {
        var services = new ServiceCollection();
        services.AddSingleton(unitOfWork);
        services.AddSingleton<IAuditTrail>(new StagingAuditTrail(unitOfWork));

        return services
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });

                registry.AddCommands(builder =>
                {
                    builder.Register<TransactionalCommand>();
                    builder.Register<TransactionalCommandHandler>();
                    builder.Register<TransactionalCommandDefinition>();
                    builder.Register<UnitOfWorkCommitCompletionHandler>();
                    builder.EnableAuditing();
                });
            })
            .BuildServiceProvider();
    }

    [Fact]
    public async Task A_staged_audit_record_is_flushed_by_the_commit_that_follows_it()
    {
        var unitOfWork = new FakeUnitOfWork();
        var provider = BuildProvider(unitOfWork);

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new TransactionalCommand()).ConfigureAwait(false);

        unitOfWork.Committed.Should().BeTrue();
        unitOfWork.RolledBack.Should().BeFalse();

        // The domain write was staged by the handler and the record by the audit writer, and both left the unit of work
        // in the same commit, which is the guarantee the pattern exists to provide.
        unitOfWork.Flushed.Should().Equal("order-placed", "audit:orders.place-order:Succeeded");
    }

    [Fact]
    public async Task A_failed_mediation_rolls_the_staged_record_back_with_the_change()
    {
        var unitOfWork = new FakeUnitOfWork();
        var provider = BuildProvider(unitOfWork);

        var act = async () => await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new TransactionalCommand { ShouldThrow = true }).ConfigureAwait(false);

        await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);

        unitOfWork.Committed.Should().BeFalse();
        unitOfWork.RolledBack.Should().BeTrue();
        unitOfWork.Flushed.Should().BeEmpty();

        // A record for a failure cannot ride the transaction being rolled back, so the trail writes it out of band.
        unitOfWork.OutOfBand.Should().Equal("audit:orders.place-order:Failed");
    }

    [Fact]
    public async Task A_commit_failure_reaches_the_caller()
    {
        var unitOfWork = new FakeUnitOfWork { FailOnCommit = true };
        var provider = BuildProvider(unitOfWork);

        var act = async () => await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new TransactionalCommand()).ConfigureAwait(false);

        // The completion stage swallows a handler fault only when the mediation had already failed, so a commit placed
        // there still reports a conflict to the caller rather than silently losing the write.
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("commit failed").ConfigureAwait(false);
    }
}
