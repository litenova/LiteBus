using LiteBus.Commands;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Queries;
using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;
using ICommand = LiteBus.Commands.Abstractions.ICommand;

namespace LiteBus.Mediator.UnitTests.Metadata;

/// <summary>
///     Verifies the describe-style definition shape, requirements scoped to an axis or a marker, and the application
///     composition hook.
/// </summary>
/// <remarks>
///     The keyed shape forces every declaration after the first to be written as an explicit interface implementation
///     naming the message type and the value type again. An unscoped requirement demanded exemptions from every query
///     that said nothing. Both are ergonomic defects that made the declaration model cost more than it returned.
/// </remarks>
[Collection("Sequential")]
public sealed class DescribeDefinitionTests : LiteBusTestBase
{
    [Fact]
    public void One_describe_method_declares_several_values_for_one_message()
    {
        var provider = BuildProvider();
        var metadata = provider.GetRequiredService<IMessageMetadataAccessor>().ForMessage<TransferFundsCommand>();

        metadata.TryGet<AuditDeclaration>(out var audit).Should().BeTrue();
        (audit as AuditedDeclaration)!.Action.Should().Be("money.transfer-funds");
        (audit as AuditedDeclaration)!.Category.Should().Be("money");
        (audit as AuditedDeclaration)!.TargetKind.Should().Be("account");

        metadata.TryGet<RequiredAuthorization>(out var permission).Should().BeTrue();
        permission!.Name.Should().Be("money.transfer");
    }

    [Fact]
    public void Describe_records_an_audit_exemption_and_an_application_exemption_in_one_set()
    {
        var provider = BuildProvider();
        var metadata = provider.GetRequiredService<IMessageMetadataAccessor>().ForMessage<PingCommand>();

        metadata.TryGet<DeclarationExemptions>(out var exemptions).Should().BeTrue();

        // One place to read every exemption, whichever feature recorded it.
        exemptions!.Covers<AuditDeclaration>().Should().BeTrue();
        exemptions.Covers<RequiredAuthorization>().Should().BeTrue();

        // The audit position is still a value, because that is what the record writer reads.
        metadata.TryGet<AuditDeclaration>(out var audit).Should().BeTrue();
        audit.Should().BeOfType<AuditExemptDeclaration>();
    }

    [Fact]
    public void A_requirement_scoped_to_a_marker_leaves_the_messages_outside_it_alone()
    {
        // 26 exemptions that said nothing was the cost of the unscoped form. Scoping to the marker that carries the
        // acting account means only the commands the rule is about have to answer for it.
        var act = () => Build(messaging =>
            messaging.RequireDeclaration<RequiredAuthorization, IAttributedCommand>());

        act.Should().NotThrow();
    }

    [Fact]
    public void A_requirement_scoped_to_a_marker_still_reports_a_message_inside_it()
    {
        var act = () => Build(
            messaging => messaging.RequireDeclaration<RequiredAuthorization, IAttributedCommand>(),
            commands => commands.Register<UnattributedTransferCommand>());

        act.Should().Throw<LiteBusConfigurationException>()
            .WithMessage("*RequiredAuthorization is required of every IAttributedCommand*UnattributedTransferCommand*");
    }

    [Fact]
    public void A_requirement_scoped_to_the_command_axis_does_not_reach_queries()
    {
        // The reported case: requiring a permission of commands used to demand one from all 26 queries.
        var act = () => Build(messaging => messaging.RequireDeclaration<AuditDeclaration, ICommand>());

        act.Should().Throw<LiteBusConfigurationException>()
            .WithMessage("*every ICommand*");

        var thrown = Record.Exception(() =>
            Build(messaging => messaging.RequireDeclaration<AuditDeclaration, ICommand>()));

        thrown!.Message.Should().NotContain(nameof(ListAccountsQuery));
    }

    [Fact]
    public void An_unscoped_requirement_still_covers_every_message()
    {
        var act = () => Build(messaging => messaging.RequireDeclaration<RequiredAuthorization>());

        act.Should().Throw<LiteBusConfigurationException>()
            .WithMessage("*every registered message*");
    }

    [Fact]
    public void A_predicate_requirement_names_its_own_scope_in_the_error()
    {
        var act = () => Build(messaging => messaging.RequireDeclaration<RequiredAuthorization>(
            messageType => messageType.Name.StartsWith("Sweep", StringComparison.Ordinal),
            "every sweep command"));

        // A predicate cannot describe itself, so the description is what makes the error read as a policy.
        act.Should().Throw<LiteBusConfigurationException>()
            .WithMessage("*required of every sweep command*SweepStaleLocksCommand*");
    }

    [Fact]
    public void Declaring_one_value_type_twice_in_one_describe_is_reported()
    {
        var act = () => Build(
            _ => { },
            commands =>
            {
                commands.Register<DoubleDescribedCommand>();
                commands.Register<DoubleDescribedCommandHandler>();
                commands.Register<DoubleDescribedCommandDefinition>();
            });

        act.Should().Throw<LiteBusConfigurationException>()
            .WithMessage("*declares 'RequiredAuthorization' twice*DoubleDescribedCommand*");
    }

    [Fact]
    public void A_describe_that_declares_nothing_is_reported()
    {
        var act = () => Build(
            _ => { },
            commands =>
            {
                commands.Register<DoubleDescribedCommand>();
                commands.Register<DoubleDescribedCommandHandler>();
                commands.Register<EmptyDescribedCommandDefinition>();
            });

        act.Should().Throw<LiteBusConfigurationException>()
            .WithMessage("*declared nothing*");
    }

    [Fact]
    public void A_composition_check_asserts_over_every_registered_message()
    {
        var seen = new List<string>();

        Build(messaging => messaging.ValidateComposition(catalog =>
        {
            foreach (var entry in catalog.Audited())
            {
                seen.Add(entry.Audit!.Action);
            }
        }));

        // The five-line assertion that was previously unreachable without authoring a module.
        seen.Should().Contain("money.transfer-funds");
    }

    [Fact]
    public void The_catalog_holds_every_registered_message_and_filters_the_audited_ones()
    {
        IMessageCatalog? captured = null;
        Build(messaging => messaging.ValidateComposition(catalog => captured = catalog));

        captured.Should().NotBeNull();
        captured!.Count.Should().BeGreaterThan(0);

        // Enumerating gives every message; Audited() gives the ones a catalogue is built from.
        captured.Select(entry => entry.MessageType).Should().Contain(typeof(TransferFundsCommand));
        captured.Select(entry => entry.MessageType).Should().Contain(typeof(ListAccountsQuery));

        var audited = captured.Audited().ToList();
        audited.Select(entry => entry.MessageType).Should().Contain(typeof(TransferFundsCommand));

        // An exempt message takes a position and is still absent, because a catalogue of audited actions is what
        // this exists to build.
        audited.Select(entry => entry.MessageType).Should().NotContain(typeof(PingCommand));
        audited.Select(entry => entry.MessageType).Should().NotContain(typeof(SweepStaleLocksCommand));
        audited.Should().AllSatisfy(entry => entry.Audit.Should().NotBeNull());
    }

    [Fact]
    public void Duplicate_audit_actions_are_reported_at_composition()
    {
        var act = () => Build(
            messaging => messaging.RequireUniqueAuditActions(),
            commands =>
            {
                commands.Register<ClashingActionCommand>();
                commands.Register<ClashingActionCommandHandler>();
            });

        // Two use cases under one action code make the trail unqueryable by use case, and nothing else reports it.
        act.Should().Throw<LiteBusConfigurationException>()
            .WithMessage("*money.transfer-funds*ClashingActionCommand*");
    }

    [Fact]
    public void An_audit_action_breaking_the_format_is_reported_at_composition()
    {
        var act = () => Build(
            messaging => messaging.RequireAuditActionFormat(),
            commands =>
            {
                commands.Register<BadlyNamedCommand>();
                commands.Register<BadlyNamedCommandHandler>();
            });

        act.Should().Throw<LiteBusConfigurationException>()
            .WithMessage("*Money_TransferFundsBadly*");
    }

    [Fact]
    public void The_default_audit_action_format_accepts_the_documented_convention()
    {
        var act = () => Build(messaging => messaging.RequireAuditActionFormat());

        act.Should().NotThrow();
    }

    /// <summary>
    ///     Builds a provider over the standard set of described messages.
    /// </summary>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildProvider()
    {
        return Build(_ => { });
    }

    /// <summary>
    ///     Builds a provider with an optional extra messaging and command configuration.
    /// </summary>
    /// <param name="messaging">The extra messaging configuration.</param>
    /// <param name="commands">The extra command registrations.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider Build(
        Action<MessageModuleBuilder> messaging,
        Action<CommandModuleBuilder>? commands = null)
    {
        return new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(messaging);

                registry.AddCommands(builder =>
                {
                    builder.Register<TransferFundsCommand>();
                    builder.Register<TransferFundsCommandHandler>();
                    builder.Register<TransferFundsCommandDefinition>();
                    builder.Register<PingCommand>();
                    builder.Register<PingCommandHandler>();
                    builder.Register<PingCommandDefinition>();
                    builder.Register<SweepStaleLocksCommand>();
                    builder.Register<SweepStaleLocksCommandHandler>();
                    commands?.Invoke(builder);
                });

                registry.AddQueries(builder =>
                {
                    builder.Register<ListAccountsQuery>();
                    builder.Register<ListAccountsQueryHandler>();
                });
            })
            .BuildServiceProvider();
    }
}
