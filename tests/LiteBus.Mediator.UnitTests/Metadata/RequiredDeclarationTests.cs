using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.Metadata;

/// <summary>
///     Verifies that <c>RequireDeclaration&lt;TValue&gt;</c> fails composition for a registered message that states no
///     position on a required declaration.
/// </summary>
/// <remarks>
///     The point of the feature is to turn a written policy into a startup failure. These tests pin the two things that
///     make it usable: the check runs after every module has registered its messages, and the error names every
///     offender rather than the first.
/// </remarks>
[Collection("Sequential")]
public sealed class RequiredDeclarationTests : LiteBusTestBase
{
    /// <summary>
    ///     Builds a provider requiring <see cref="RequiredPermission" /> and registering the given command types.
    /// </summary>
    /// <param name="commandTypes">The command and definition types to register.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildProvider(params Type[] commandTypes)
    {
        return new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(messaging => messaging.RequireDeclaration<RequiredPermission>());

                registry.AddCommands(builder =>
                {
                    foreach (var commandType in commandTypes)
                    {
                        builder.Register(commandType);
                    }
                });
            })
            .BuildServiceProvider();
    }

    [Fact]
    public void A_declared_message_composes()
    {
        // The declaration is registered by AddCommands, which builds after AddMessaging, so this only passes because
        // the check is deferred until every module has built.
        var act = () => BuildProvider(
            typeof(PublishScheduleCommand),
            typeof(PublishScheduleCommandHandler),
            typeof(PublishScheduleCommandDefinition));

        act.Should().NotThrow();
    }

    [Fact]
    public void An_attribute_declaration_composes()
    {
        var act = () => BuildProvider(typeof(TouchScheduleCommand), typeof(TouchScheduleCommandHandler));

        act.Should().NotThrow();
    }

    [Fact]
    public void An_undeclared_message_fails_composition_naming_the_message_and_the_value()
    {
        var act = () => BuildProvider(typeof(DraftScheduleCommand), typeof(DraftScheduleCommandHandler));

        act.Should().Throw<LiteBusConfigurationException>()
            .WithMessage("*RequiredPermission is not declared by: DraftScheduleCommand*");
    }

    [Fact]
    public void Every_offender_is_named_rather_than_the_first()
    {
        // A requirement turned on for an existing codebase reports dozens at once. Fixing them one composition failure
        // at a time would make the feature unusable.
        var act = () => BuildProvider(
            typeof(DraftScheduleCommand),
            typeof(DraftScheduleCommandHandler),
            typeof(WithdrawScheduleCommand),
            typeof(WithdrawScheduleCommandHandler));

        act.Should().Throw<LiteBusConfigurationException>()
            .WithMessage("*DraftScheduleCommand, WithdrawScheduleCommand*");
    }

    [Fact]
    public void A_recorded_exemption_satisfies_the_requirement()
    {
        var act = () => BuildProvider(typeof(BrowseScheduleCommand), typeof(BrowseScheduleCommandHandler));

        act.Should().NotThrow();
    }

    [Fact]
    public void An_exemption_for_another_value_does_not_satisfy_this_requirement()
    {
        var act = () => BuildProvider(typeof(RetireScheduleCommand), typeof(RetireScheduleCommandHandler));

        act.Should().Throw<LiteBusConfigurationException>()
            .WithMessage("*RequiredPermission is not declared by: RetireScheduleCommand*");
    }

    [Fact]
    public void An_exemption_is_readable_with_its_rationale()
    {
        var accessor = BuildProvider(typeof(BrowseScheduleCommand), typeof(BrowseScheduleCommandHandler))
            .GetRequiredService<IMessageMetadataAccessor>();

        accessor.TryGet<BrowseScheduleCommand, DeclarationExemptions>(out var exemptions).Should().BeTrue();
        exemptions!.TryGet(typeof(RequiredPermission), out var exemption).Should().BeTrue();
        exemption!.Rationale.Should().Be("the schedule list is public");
    }

    [Fact]
    public void Several_exemptions_on_one_message_are_aggregated()
    {
        var accessor = BuildProvider(typeof(ArchiveScheduleCommand), typeof(ArchiveScheduleCommandHandler))
            .GetRequiredService<IMessageMetadataAccessor>();

        accessor.TryGet<ArchiveScheduleCommand, DeclarationExemptions>(out var exemptions).Should().BeTrue();

        // Metadata holds one value per key type, so repeated attributes have to collapse into one set instead of
        // overwriting each other.
        exemptions!.Values.Should().HaveCount(2);
        exemptions.Covers<RequiredPermission>().Should().BeTrue();
        exemptions.Covers<RetentionClass>().Should().BeTrue();
    }

    [Fact]
    public void No_requirement_means_no_check()
    {
        var act = () => new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });
                registry.AddCommands(builder =>
                {
                    builder.Register<DraftScheduleCommand>();
                    builder.Register<DraftScheduleCommandHandler>();
                });
            })
            .BuildServiceProvider();

        act.Should().NotThrow();
    }
}
