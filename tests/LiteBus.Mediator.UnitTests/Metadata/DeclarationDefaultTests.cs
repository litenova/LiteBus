using LiteBus.Commands;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;
using ICommand = LiteBus.Commands.Abstractions.ICommand;

namespace LiteBus.Mediator.UnitTests.Metadata;

/// <summary>
///     Verifies that a value declared against a marker interface covers the family beneath it and yields to a message
///     that states its own position.
/// </summary>
/// <remarks>
///     Declaring the same rule on a hundred commands is the same rule stated a hundred times, and a hundred places for
///     it to drift. Stating it once against the marker the family already shares is what these tests are about, and
///     the precedence is what makes it a default rather than an override.
/// </remarks>
[Collection("Sequential")]
public sealed class DeclarationDefaultTests : LiteBusTestBase
{
    [Fact]
    public void A_default_covers_every_message_in_the_family()
    {
        var accessor = Build(messaging => messaging
                .DeclareDefault<IAttributedCommand, RequiredAuthorization>(new RequiredAuthorization("family.default")))
            .GetRequiredService<IMessageMetadataAccessor>();

        accessor.TryGet<UnattributedTransferCommand, RequiredAuthorization>(out var inherited).Should().BeTrue();
        inherited!.Name.Should().Be("family.default");
    }

    [Fact]
    public void A_message_that_states_its_own_position_keeps_it()
    {
        var accessor = Build(messaging => messaging
                .DeclareDefault<IAttributedCommand, RequiredAuthorization>(new RequiredAuthorization("family.default")))
            .GetRequiredService<IMessageMetadataAccessor>();

        // TransferFundsCommand declares its own through a definition, so the default yields to it. That precedence is
        // the whole difference between a default and an override.
        accessor.TryGet<TransferFundsCommand, RequiredAuthorization>(out var own).Should().BeTrue();
        own!.Name.Should().Be("money.transfer");
    }

    [Fact]
    public void A_default_does_not_reach_a_message_outside_the_family()
    {
        var accessor = Build(messaging => messaging
                .DeclareDefault<IAttributedCommand, RequiredAuthorization>(new RequiredAuthorization("family.default")))
            .GetRequiredService<IMessageMetadataAccessor>();

        accessor.TryGet<SweepStaleLocksCommand, RequiredAuthorization>(out _).Should().BeFalse();
    }

    [Fact]
    public void A_default_declared_before_the_messages_are_registered_still_reaches_them()
    {
        // The messaging module builds before the axis modules, so a default is declared while the registry holds no
        // commands at all. It has to reach the messages that arrive afterwards.
        var accessor = Build(messaging => messaging
                .DeclareDefault(MessageDeclarationItem.For<ICommand, RetentionWindow>(new RetentionWindow(30))))
            .GetRequiredService<IMessageMetadataAccessor>();

        accessor.TryGet<SweepStaleLocksCommand, RetentionWindow>(out var retention).Should().BeTrue();
        retention!.Days.Should().Be(30);
    }

    [Fact]
    public void A_default_satisfies_a_requirement_scoped_to_the_same_family()
    {
        var act = () => Build(messaging => messaging
            .DeclareDefault<IAttributedCommand, RequiredAuthorization>(new RequiredAuthorization("family.default"))
            .RequireDeclaration<RequiredAuthorization, IAttributedCommand>());

        // Defaults plus overrides is what makes a scoped requirement affordable for a large family.
        act.Should().NotThrow();
    }

    [Fact]
    public void Two_defaults_for_one_scope_and_value_type_are_reported()
    {
        var act = () => Build(messaging => messaging
            .DeclareDefault<IAttributedCommand, RequiredAuthorization>(new RequiredAuthorization("first"))
            .DeclareDefault<IAttributedCommand, RequiredAuthorization>(new RequiredAuthorization("second")));

        // One of them would have to be discarded and nothing says which.
        act.Should().Throw<PipelineContractException>()
            .WithMessage("*a composition default*RequiredAuthorization*IAttributedCommand*");
    }

    [Fact]
    public void A_default_and_a_definition_for_the_same_message_are_reported()
    {
        var act = () => Build(messaging => messaging
            .DeclareDefault<TransferFundsCommand, RequiredAuthorization>(new RequiredAuthorization("duplicate")));

        // Not a default at all: declared against the message itself, alongside the definition that already declares
        // it, so neither is closer and the effective value would depend on order.
        act.Should().Throw<PipelineContractException>()
            .WithMessage("*a composition default*TransferFundsCommandDefinition*RequiredAuthorization*");
    }

    [Fact]
    public void A_value_that_is_not_an_instance_of_its_key_type_is_reported()
    {
        var act = () => Build(messaging => messaging.DeclareDefault(new MessageDeclarationItem
        {
            MessageType = typeof(ICommand),
            DeclarationType = typeof(RetentionWindow),
            Value = "not a retention window"
        }));

        // A reader looks the value up by its key type, so a mismatch would find nothing.
        act.Should().Throw<MessageDeclarationException>()
            .WithMessage("*not assignable*");
    }

    [Fact]
    public void A_default_rejects_a_null_value()
    {
        var act = () => MessageDeclarationItem.For<ICommand, RetentionWindow>(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    /// <summary>
    ///     Builds a provider over the described commands with the given messaging configuration.
    /// </summary>
    /// <param name="messaging">The messaging configuration under test.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider Build(Action<MessageModuleBuilder> messaging)
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
                });
            })
            .BuildServiceProvider();
    }
}
