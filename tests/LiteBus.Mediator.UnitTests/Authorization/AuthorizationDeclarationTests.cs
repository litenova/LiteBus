using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.Authorization;

/// <summary>
///     Verifies that a message states its authorization position the way it states its audit position, and that a
///     requirement over <see cref="AuthorizationDeclaration" /> fails composition for a message that states none.
/// </summary>
/// <remarks>
///     LiteBus declares the position and never authorizes anything. These tests pin the declaration half: every shape
///     lands on the same metadata key, the requirement reads it, and a blank policy or rationale is rejected where it
///     is written rather than travelling to a guard as an empty string.
/// </remarks>
[Collection("Sequential")]
public sealed class AuthorizationDeclarationTests : LiteBusTestBase
{
    /// <summary>
    ///     Builds a provider requiring an authorization declaration of every command and registering the given types.
    /// </summary>
    /// <param name="commandTypes">The command, handler, and definition types to register.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildProvider(params Type[] commandTypes)
    {
        return new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(messaging =>
                    messaging.RequireDeclaration<AuthorizationDeclaration, ICommand>());

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
    ///     Verifies that <c>[Authorized]</c> lands on the <see cref="AuthorizationDeclaration" /> key a guard reads.
    /// </summary>
    [Fact]
    public void An_authorized_attribute_is_readable_as_an_authorized_declaration()
    {
        var accessor = BuildProviderWithoutRequirement(typeof(OpenVaultCommand), typeof(OpenVaultCommandHandler))
            .GetRequiredService<IMessageMetadataAccessor>();

        accessor.TryGet<OpenVaultCommand, AuthorizationDeclaration>(out var declaration).Should().BeTrue();
        declaration.Should().BeOfType<AuthorizedDeclaration>()
            .Which.Policy.Should().Be("vaults.open");
        declaration!.RequiresAuthorization.Should().BeTrue();
    }

    /// <summary>
    ///     Verifies that <c>[AuthorizationExempt]</c> reads back as an exempt declaration with its rationale.
    /// </summary>
    [Fact]
    public void An_authorization_exempt_attribute_is_readable_as_an_exempt_declaration()
    {
        var accessor = BuildProviderWithoutRequirement(
                typeof(BrowseVaultCatalogueCommand),
                typeof(BrowseVaultCatalogueCommandHandler))
            .GetRequiredService<IMessageMetadataAccessor>();

        accessor.TryGet<BrowseVaultCatalogueCommand, AuthorizationDeclaration>(out var declaration).Should().BeTrue();
        declaration.Should().BeOfType<AuthorizationExemptDeclaration>()
            .Which.Rationale.Should().Be("the vault catalogue is public, so there is no actor to authorize");
        declaration!.RequiresAuthorization.Should().BeFalse();
    }

    /// <summary>
    ///     Verifies that <c>[AuthorizationExempt]</c> also records the exemption a requirement reads.
    /// </summary>
    [Fact]
    public void An_authorization_exempt_attribute_records_the_exemption_alongside_the_declaration()
    {
        var accessor = BuildProviderWithoutRequirement(
                typeof(BrowseVaultCatalogueCommand),
                typeof(BrowseVaultCatalogueCommandHandler))
            .GetRequiredService<IMessageMetadataAccessor>();

        // The declaration is what a guard reads; the exemption is what every requirement and catalogue reads.
        accessor.TryGet<BrowseVaultCatalogueCommand, DeclarationExemptions>(out var exemptions).Should().BeTrue();
        exemptions!.Covers<AuthorizationDeclaration>().Should().BeTrue();
    }

    /// <summary>
    ///     Verifies that a keyed definition lands on the same key an attribute would use.
    /// </summary>
    [Fact]
    public void A_keyed_definition_is_readable_as_an_authorized_declaration()
    {
        var accessor = BuildProviderWithoutRequirement(
                typeof(RotateVaultKeyCommand),
                typeof(RotateVaultKeyCommandHandler),
                typeof(RotateVaultKeyCommandDefinition))
            .GetRequiredService<IMessageMetadataAccessor>();

        accessor.TryGet<RotateVaultKeyCommand, AuthorizationDeclaration>(out var declaration).Should().BeTrue();
        declaration.Should().BeOfType<AuthorizedDeclaration>()
            .Which.Policy.Should().Be("vaults.rotate");
    }

    /// <summary>
    ///     Verifies that the <c>Describe</c> authorized helper declares the same value a definition would.
    /// </summary>
    [Fact]
    public void A_describe_authorized_helper_is_readable_as_an_authorized_declaration()
    {
        var accessor = BuildProviderWithoutRequirement(
                typeof(SealVaultCommand),
                typeof(SealVaultCommandHandler),
                typeof(SealVaultCommandDefinition))
            .GetRequiredService<IMessageMetadataAccessor>();

        accessor.TryGet<SealVaultCommand, AuthorizationDeclaration>(out var declaration).Should().BeTrue();
        declaration.Should().BeOfType<AuthorizedDeclaration>()
            .Which.Policy.Should().Be("vaults.seal");
    }

    /// <summary>
    ///     Verifies that the <c>Describe</c> exempt helper records both the declaration and the exemption.
    /// </summary>
    [Fact]
    public void A_describe_exempt_helper_records_the_declaration_and_the_exemption()
    {
        var accessor = BuildProviderWithoutRequirement(
                typeof(PingVaultCommand),
                typeof(PingVaultCommandHandler),
                typeof(PingVaultCommandDefinition))
            .GetRequiredService<IMessageMetadataAccessor>();

        var metadata = accessor.ForMessage<PingVaultCommand>();

        metadata.TryGet<AuthorizationDeclaration>(out var declaration).Should().BeTrue();
        declaration.Should().BeOfType<AuthorizationExemptDeclaration>()
            .Which.Rationale.Should().Be("a liveness probe has no actor to authorize");

        metadata.TryGet<DeclarationExemptions>(out var exemptions).Should().BeTrue();
        exemptions!.Covers<AuthorizationDeclaration>().Should().BeTrue();
    }

    /// <summary>
    ///     Verifies that a command stating no authorization position fails composition under the requirement.
    /// </summary>
    [Fact]
    public void An_undeclared_command_fails_composition_naming_the_message_and_the_value()
    {
        var act = () => BuildProvider(typeof(DraftVaultCommand), typeof(DraftVaultCommandHandler));

        act.Should().Throw<LiteBusConfigurationException>()
            .WithMessage("*AuthorizationDeclaration is required of every ICommand*DraftVaultCommand*");
    }

    /// <summary>
    ///     Verifies that every declaration shape satisfies the requirement at composition.
    /// </summary>
    [Fact]
    public void Every_declared_shape_composes_under_the_requirement()
    {
        var act = () => BuildProvider(
            typeof(OpenVaultCommand),
            typeof(OpenVaultCommandHandler),
            typeof(BrowseVaultCatalogueCommand),
            typeof(BrowseVaultCatalogueCommandHandler),
            typeof(RotateVaultKeyCommand),
            typeof(RotateVaultKeyCommandHandler),
            typeof(RotateVaultKeyCommandDefinition),
            typeof(SealVaultCommand),
            typeof(SealVaultCommandHandler),
            typeof(SealVaultCommandDefinition),
            typeof(PingVaultCommand),
            typeof(PingVaultCommandHandler),
            typeof(PingVaultCommandDefinition));

        act.Should().NotThrow();
    }

    /// <summary>
    ///     Verifies that a blank policy is rejected by the factory rather than travelling to a guard.
    /// </summary>
    /// <param name="policy">The blank policy under test.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_policy_is_rejected_where_it_is_written(string? policy)
    {
        var act = () => AuthorizationDeclaration.Authorized(policy!);

        act.Should().Throw<ArgumentException>();
    }

    /// <summary>
    ///     Verifies that a blank rationale is rejected by the factory rather than recorded as empty.
    /// </summary>
    /// <param name="rationale">The blank rationale under test.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_rationale_is_rejected_where_it_is_written(string? rationale)
    {
        var act = () => AuthorizationDeclaration.Exempt(rationale!);

        act.Should().Throw<ArgumentException>();
    }

    /// <summary>
    ///     Verifies that the exemption attribute rejects a blank rationale in its constructor.
    /// </summary>
    [Fact]
    public void A_blank_attribute_rationale_is_rejected_by_the_attribute()
    {
        var act = () => new AuthorizationExemptAttribute("   ");

        act.Should().Throw<ArgumentException>();
    }

    /// <summary>
    ///     Verifies that a blank attribute policy is rejected when the declaration is created.
    /// </summary>
    [Fact]
    public void A_blank_attribute_policy_is_rejected_when_the_declaration_is_created()
    {
        // The attribute mirrors AuditedAttribute: the policy rides the attribute untouched and is validated when it is
        // converted to the declaration, which is what registration collects.
        var act = () => new AuthorizedAttribute("   ").CreateDeclaration();

        act.Should().Throw<ArgumentException>();
    }
}
