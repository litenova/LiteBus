using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Tenancy;
using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.Tenancy;

/// <summary>
///     Verifies that a message states its tenancy position the way it states its authorization position, and that a
///     requirement over <see cref="TenancyDeclaration" /> fails composition for a message that states none.
/// </summary>
/// <remarks>
///     LiteBus declares the position and enforces it in the tenancy guard. These tests pin the declaration half:
///     every shape lands on the same metadata key, the requirement reads it, and a blank reason is rejected where it
///     is written rather than travelling to a guard as an empty string.
/// </remarks>
[Collection("Sequential")]
public sealed class TenancyDeclarationTests : LiteBusTestBase
{
    /// <summary>
    ///     Builds a provider requiring a tenancy declaration of every command and registering the given types.
    /// </summary>
    /// <param name="commandTypes">The command, handler, and definition types to register.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildProvider(params Type[] commandTypes)
    {
        return new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(messaging =>
                    messaging.RequireDeclaration<TenancyDeclaration, ICommand>());

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

    /// <summary>
    ///     Builds a provider without the requirement, for reading declarations off messages in isolation.
    /// </summary>
    /// <param name="commandTypes">The command, handler, and definition types to register.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildProviderWithoutRequirement(params Type[] commandTypes)
    {
        return new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });

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

    /// <summary>
    ///     Verifies that <c>[TenantRequired]</c> lands on the <see cref="TenancyDeclaration" /> key a guard reads.
    /// </summary>
    [Fact]
    public void A_tenant_required_attribute_is_readable_as_a_required_declaration()
    {
        var accessor = BuildProviderWithoutRequirement(typeof(PostWorkspaceEntryCommand), typeof(PostWorkspaceEntryCommandHandler))
            .GetRequiredService<IMessageMetadataAccessor>();

        accessor.TryGet<PostWorkspaceEntryCommand, TenancyDeclaration>(out var declaration).Should().BeTrue();
        declaration.Should().BeOfType<TenantRequiredDeclaration>();
        declaration!.RequiresTenant.Should().BeTrue();
    }

    /// <summary>
    ///     Verifies that <c>[CrossTenant]</c> reads back as a cross-tenant declaration with its reason.
    /// </summary>
    [Fact]
    public void A_cross_tenant_attribute_is_readable_as_a_cross_tenant_declaration()
    {
        var accessor = BuildProviderWithoutRequirement(typeof(PurgeExpiredEntriesCommand), typeof(PurgeExpiredEntriesCommandHandler))
            .GetRequiredService<IMessageMetadataAccessor>();

        accessor.TryGet<PurgeExpiredEntriesCommand, TenancyDeclaration>(out var declaration).Should().BeTrue();
        declaration.Should().BeOfType<CrossTenantDeclaration>()
            .Which.Reason.Should().Be("the retention job purges expired entries across every workspace");
        declaration!.RequiresTenant.Should().BeFalse();
    }

    /// <summary>
    ///     Verifies that <c>[NoTenant]</c> reads back as a no-tenant declaration with its reason.
    /// </summary>
    [Fact]
    public void A_no_tenant_attribute_is_readable_as_a_no_tenant_declaration()
    {
        var accessor = BuildProviderWithoutRequirement(typeof(SignInCommand), typeof(SignInCommandHandler))
            .GetRequiredService<IMessageMetadataAccessor>();

        accessor.TryGet<SignInCommand, TenancyDeclaration>(out var declaration).Should().BeTrue();
        declaration.Should().BeOfType<NoTenantDeclaration>()
            .Which.Reason.Should().Be("sign-in creates the session, so there is no tenant yet");
        declaration!.RequiresTenant.Should().BeFalse();
    }

    /// <summary>
    ///     Verifies that a keyed definition lands on the same key an attribute would use.
    /// </summary>
    [Fact]
    public void A_keyed_definition_is_readable_as_a_required_declaration()
    {
        var accessor = BuildProviderWithoutRequirement(
                typeof(RotateWorkspaceKeyCommand),
                typeof(RotateWorkspaceKeyCommandHandler),
                typeof(RotateWorkspaceKeyCommandTenancyDefinition))
            .GetRequiredService<IMessageMetadataAccessor>();

        accessor.TryGet<RotateWorkspaceKeyCommand, TenancyDeclaration>(out var declaration).Should().BeTrue();
        declaration.Should().BeOfType<TenantRequiredDeclaration>();
        declaration!.RequiresTenant.Should().BeTrue();
    }

    /// <summary>
    ///     Verifies that a command stating no tenancy position fails composition under the requirement.
    /// </summary>
    [Fact]
    public void An_undeclared_command_fails_composition_naming_the_message_and_the_value()
    {
        var act = () => BuildProvider(typeof(DraftWorkspaceNoticeCommand), typeof(DraftWorkspaceNoticeCommandHandler));

        act.Should().Throw<LiteBusConfigurationException>()
            .WithMessage("*TenancyDeclaration is required of every ICommand*DraftWorkspaceNoticeCommand*");
    }

    /// <summary>
    ///     Verifies that every declaration shape satisfies the requirement at composition.
    /// </summary>
    [Fact]
    public void Every_declared_shape_composes_under_the_requirement()
    {
        var act = () => BuildProvider(
            typeof(PostWorkspaceEntryCommand),
            typeof(PostWorkspaceEntryCommandHandler),
            typeof(PurgeExpiredEntriesCommand),
            typeof(PurgeExpiredEntriesCommandHandler),
            typeof(SignInCommand),
            typeof(SignInCommandHandler),
            typeof(RotateWorkspaceKeyCommand),
            typeof(RotateWorkspaceKeyCommandHandler),
            typeof(RotateWorkspaceKeyCommandTenancyDefinition));

        act.Should().NotThrow();
    }

    /// <summary>
    ///     Verifies that a blank cross-tenant reason is rejected by the factory rather than travelling to a guard.
    /// </summary>
    /// <param name="reason">The blank reason under test.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_cross_tenant_reason_is_rejected_where_it_is_written(string? reason)
    {
        var act = () => TenancyDeclaration.CrossTenant(reason!);

        act.Should().Throw<ArgumentException>();
    }

    /// <summary>
    ///     Verifies that a blank no-tenant reason is rejected by the factory rather than travelling to a guard.
    /// </summary>
    /// <param name="reason">The blank reason under test.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_no_tenant_reason_is_rejected_where_it_is_written(string? reason)
    {
        var act = () => TenancyDeclaration.None(reason!);

        act.Should().Throw<ArgumentException>();
    }

    /// <summary>
    ///     Verifies that the cross-tenant attribute rejects a blank reason in its constructor.
    /// </summary>
    [Fact]
    public void A_blank_cross_tenant_attribute_reason_is_rejected_by_the_attribute()
    {
        var act = () => new CrossTenantAttribute("   ");

        act.Should().Throw<ArgumentException>();
    }

    /// <summary>
    ///     Verifies that the no-tenant attribute rejects a blank reason in its constructor.
    /// </summary>
    [Fact]
    public void A_blank_no_tenant_attribute_reason_is_rejected_by_the_attribute()
    {
        var act = () => new NoTenantAttribute("   ");

        act.Should().Throw<ArgumentException>();
    }
}
