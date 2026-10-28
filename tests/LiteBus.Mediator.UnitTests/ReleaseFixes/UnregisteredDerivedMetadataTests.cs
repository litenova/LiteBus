using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Mediator.UnitTests.Auditing;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.ReleaseFixes;

/// <summary>
///     Verifies that an unregistered runtime type mediated through a base descriptor reads that descriptor's metadata,
///     so auditing, guards, and idempotency never depend on scan coverage.
/// </summary>
[Collection("Sequential")]
public sealed class UnregisteredDerivedMetadataTests : LiteBusTestBase
{
    [Fact]
    public async Task Unregistered_derived_command_is_audited_under_the_base_declaration()
    {
        var trail = new RecordingAuditTrail();
        var provider = BuildProvider(trail);

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new LedgerDerivedCommand()).ConfigureAwait(false);

        var record = trail.Records.Should().ContainSingle().Subject;
        record.Action.Should().Be("ledger.write");
        record.Outcome.Should().Be(AuditOutcome.Succeeded);
    }

    [Fact]
    public void Metadata_accessor_reads_an_unregistered_derived_type_through_its_base()
    {
        var trail = new RecordingAuditTrail();
        var accessor = BuildProvider(trail).GetRequiredService<IMessageMetadataAccessor>();

        var metadata = accessor.ForMessage(typeof(LedgerDerivedCommand));

        metadata.TryGet<AuditDeclaration>(out var declaration).Should().BeTrue();
        declaration.Should().BeOfType<AuditedDeclaration>().Which.Action.Should().Be("ledger.write");
    }

    [Fact]
    public void Metadata_accessor_still_reports_a_type_nothing_covers()
    {
        var trail = new RecordingAuditTrail();
        var accessor = BuildProvider(trail).GetRequiredService<IMessageMetadataAccessor>();

        var act = () => accessor.ForMessage(typeof(string));

        act.Should().Throw<MessageMetadataNotFoundException>()
            .Which.MessageType.Should().Be<string>();
    }

    /// <summary>
    ///     Builds a provider registering only the base command, so the derived command mediates through it.
    /// </summary>
    /// <param name="trail">The recording trail to register.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildProvider(RecordingAuditTrail trail)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAuditTrail>(trail);

        return services
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });

                registry.AddCommands(builder =>
                {
                    builder.Register<LedgerBaseCommand>();
                    builder.Register<LedgerBaseCommandHandler>();
                    builder.Register<LedgerBaseCommandDefinition>();
                    builder.EnableAuditing();
                });
            })
            .BuildServiceProvider();
    }
}

/// <summary>
///     A base command carrying the audit declaration the family shares.
/// </summary>
internal class LedgerBaseCommand : ICommand;

/// <summary>
///     A derived command the composition never registers, mediated through its base descriptor.
/// </summary>
internal sealed class LedgerDerivedCommand : LedgerBaseCommand;

/// <summary>
///     Handles the base command, and through it the unregistered derived command.
/// </summary>
internal sealed class LedgerBaseCommandHandler : ICommandHandler<LedgerBaseCommand>
{
    /// <inheritdoc />
    public Task HandleAsync(LedgerBaseCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        return Task.CompletedTask;
    }
}

/// <summary>
///     Declares the audit position of the base command, which the derived command inherits.
/// </summary>
internal sealed class LedgerBaseCommandDefinition : IAuditDefinition<LedgerBaseCommand>
{
    /// <inheritdoc />
    public AuditDeclaration Audit => AuditDeclaration.Audited("ledger.write");
}
