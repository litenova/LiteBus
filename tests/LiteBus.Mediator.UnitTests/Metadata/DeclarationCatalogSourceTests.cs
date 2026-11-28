using LiteBus.Commands;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.Metadata;

/// <summary>
///     Verifies that one declaration source describes many messages through the definition vocabulary, with the same
///     conflict rule and composition reporting.
/// </summary>
[Collection("Sequential")]
public sealed class DeclarationCatalogSourceTests : LiteBusTestBase
{
    [Fact]
    public void One_source_declares_two_messages()
    {
        var accessor = Build(
                _ => { },
                commands => commands.Register<FamilyCatalogSource>())
            .GetRequiredService<IMessageMetadataAccessor>();

        accessor.TryGet<SweepStaleLocksCommand, RequiredAuthorization>(out var sweep).Should().BeTrue();
        sweep!.Name.Should().Be("locks.sweep");

        accessor.TryGet<UnattributedTransferCommand, RequiredAuthorization>(out var transfer).Should().BeTrue();
        transfer!.Name.Should().Be("money.transfer-source");
    }

    [Fact]
    public void The_composition_model_names_the_source()
    {
        ICompositionModel? captured = null;

        Build(
            messaging => messaging.ValidateComposition(model => captured = model),
            commands => commands.Register<FamilyCatalogSource>());

        captured.Should().NotBeNull();

        var record = captured!.DeclarationsFor(typeof(SweepStaleLocksCommand))
            .Single(candidate => candidate.DeclarationType == typeof(RequiredAuthorization));

        record.Source.Should().Be(DeclarationSourceKind.Source);
        record.DeclaredFor.Should().Be<SweepStaleLocksCommand>();
        record.SourceType.Should().Be<FamilyCatalogSource>();
        record.Value.Should().Be(new RequiredAuthorization("locks.sweep"));
    }

    [Fact]
    public void A_source_satisfies_a_requirement()
    {
        var act = () => Build(
            messaging => messaging.RequireDeclaration<RequiredAuthorization, SweepStaleLocksCommand>(),
            commands => commands.Register<FamilyCatalogSource>());

        act.Should().NotThrow();
    }

    [Fact]
    public void A_source_and_a_definition_for_the_same_message_conflict()
    {
        var act = () => Build(
            _ => { },
            commands => commands.Register<ClashingCatalogSource>());

        act.Should().Throw<PipelineContractException>()
            .WithMessage("*TransferFundsCommandDefinition*ClashingCatalogSource*RequiredAuthorization*");
    }

    [Fact]
    public void An_identical_repeat_is_a_no_op()
    {
        var act = () => Build(
            _ => { },
            commands => commands.Register<MatchingCatalogSource>());

        act.Should().NotThrow();
    }

    [Fact]
    public void A_catalog_replace_overwrites_a_definition()
    {
        var accessor = Build(
                _ => { },
                commands => commands.Register<ReplacingCatalogSource>())
            .GetRequiredService<IMessageMetadataAccessor>();

        accessor.TryGet<TransferFundsCommand, RequiredAuthorization>(out var permission).Should().BeTrue();
        permission!.Name.Should().Be("money.transfer-replaced");
    }

    [Fact]
    public void A_builder_replace_overwrites_a_default()
    {
        var accessor = Build(
                messaging => messaging
                    .DeclareDefault<SweepStaleLocksCommand, RequiredAuthorization>(new RequiredAuthorization("first"))
                    .ReplaceDeclaration<SweepStaleLocksCommand, RequiredAuthorization>(new RequiredAuthorization("second")))
            .GetRequiredService<IMessageMetadataAccessor>();

        accessor.TryGet<SweepStaleLocksCommand, RequiredAuthorization>(out var permission).Should().BeTrue();
        permission!.Name.Should().Be("second");
    }

    [Fact]
    public void Declaring_one_value_twice_in_one_source_is_reported()
    {
        var act = () => Build(
            _ => { },
            commands => commands.Register<DoubleDeclaringCatalogSource>());

        act.Should().Throw<LiteBusConfigurationException>()
            .WithMessage("*declares 'RequiredAuthorization' twice*SweepStaleLocksCommand*");
    }

    [Fact]
    public void A_source_that_declares_nothing_is_reported()
    {
        var act = () => Build(
            _ => { },
            commands => commands.Register<EmptyCatalogSource>());

        act.Should().Throw<LiteBusConfigurationException>()
            .WithMessage("*declared nothing*");
    }

    /// <summary>
    ///     Builds a provider over the described commands with the given messaging and command configuration.
    /// </summary>
    /// <param name="messaging">The messaging configuration under test.</param>
    /// <param name="commands">The extra command registrations.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider Build(Action<MessageModuleBuilder> messaging, Action<CommandModuleBuilder>? commands = null)
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
                    builder.Register<UnattributedTransferCommand>();
                    builder.Register<UnattributedTransferCommandHandler>();
                    builder.Register<SweepStaleLocksCommand>();
                    builder.Register<SweepStaleLocksCommandHandler>();
                    commands?.Invoke(builder);
                });
            })
            .BuildServiceProvider();
    }
}
