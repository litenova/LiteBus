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
///     Verifies that a convention derives a metadata value for every message that states none, and yields to every
///     source that states one.
/// </summary>
/// <remarks>
///     A convention says how a value is derived rather than what a family declares. These tests pin the precedence
///     that makes it a fallback: definition, then attribute, then default, then convention.
/// </remarks>
[Collection("Sequential")]
public sealed class DeclarationConventionTests : LiteBusTestBase
{
    [Fact]
    public void A_convention_supplies_a_value_when_nothing_else_states_one()
    {
        var accessor = Build(messaging => messaging
                .DeclareConvention<RequiredPermission>(messageType => new RequiredPermission("convention." + messageType.Name)))
            .GetRequiredService<IMessageMetadataAccessor>();

        accessor.TryGet<DraftScheduleCommand, RequiredPermission>(out var derived).Should().BeTrue();
        derived!.Name.Should().Be("convention.DraftScheduleCommand");
    }

    [Fact]
    public void A_definition_beats_a_convention()
    {
        var accessor = Build(messaging => messaging
                .DeclareConvention<RequiredPermission>(messageType => new RequiredPermission("convention." + messageType.Name)))
            .GetRequiredService<IMessageMetadataAccessor>();

        // PublishScheduleCommand declares its own through a definition, so the convention yields to it.
        accessor.TryGet<PublishScheduleCommand, RequiredPermission>(out var own).Should().BeTrue();
        own!.Name.Should().Be("schedules.publish");
    }

    [Fact]
    public void An_attribute_beats_a_convention()
    {
        var accessor = Build(messaging => messaging
                .DeclareConvention<RequiredPermission>(messageType => new RequiredPermission("convention." + messageType.Name)))
            .GetRequiredService<IMessageMetadataAccessor>();

        // TouchScheduleCommand declares its own through an attribute, so the convention yields to it.
        accessor.TryGet<TouchScheduleCommand, RequiredPermission>(out var own).Should().BeTrue();
        own!.Name.Should().Be("schedules.touch");
    }

    [Fact]
    public void A_default_beats_a_convention()
    {
        var accessor = Build(messaging => messaging
                .DeclareDefault<ICommand, RequiredPermission>(new RequiredPermission("family.default"))
                .DeclareConvention<RequiredPermission>(messageType => new RequiredPermission("convention." + messageType.Name)))
            .GetRequiredService<IMessageMetadataAccessor>();

        accessor.TryGet<DraftScheduleCommand, RequiredPermission>(out var inherited).Should().BeTrue();
        inherited!.Name.Should().Be("family.default");
    }

    [Fact]
    public void A_convention_returning_null_leaves_the_message_undeclared()
    {
        var accessor = Build(messaging => messaging
                .DeclareConvention<RequiredPermission>(messageType => null))
            .GetRequiredService<IMessageMetadataAccessor>();

        // Returning null is how a convention says it does not apply, so the message stays undeclared.
        accessor.TryGet<DraftScheduleCommand, RequiredPermission>(out _).Should().BeFalse();
    }

    [Fact]
    public void A_convention_can_key_only_the_messages_carrying_a_key()
    {
        var accessor = Build(messaging => messaging
                .DeclareConvention<IdempotencyDeclaration>(messageType =>
                    typeof(IHasIdempotencyKey).IsAssignableFrom(messageType)
                        ? IdempotencyDeclaration.KeyedBy<IHasIdempotencyKey>(message => message.IdempotencyKey)
                        : null))
            .GetRequiredService<IMessageMetadataAccessor>();

        accessor.TryGet<KeyedTransferCommand, IdempotencyDeclaration>(out var keyed).Should().BeTrue();
        var keyedDeclaration = keyed.Should().BeOfType<KeyedIdempotencyDeclaration>().Subject;
        keyedDeclaration.KeySelector.Should().NotBeNull();
        keyedDeclaration.KeySelector!(new KeyedTransferCommand { IdempotencyKey = "key-1" }).Should().Be("key-1");
        accessor.TryGet<DraftScheduleCommand, IdempotencyDeclaration>(out _).Should().BeFalse();
    }

    [Fact]
    public void A_convention_does_not_override_an_exemption()
    {
        var accessor = Build(messaging => messaging
                .DeclareConvention<RequiredPermission>(messageType => new RequiredPermission("convention." + messageType.Name)))
            .GetRequiredService<IMessageMetadataAccessor>();

        // BrowseScheduleCommand deliberately states nothing, and a derived value would silently overturn that decision.
        accessor.TryGet<BrowseScheduleCommand, RequiredPermission>(out _).Should().BeFalse();
        accessor.TryGet<BrowseScheduleCommand, DeclarationExemptions>(out var exemptions).Should().BeTrue();
        exemptions!.Covers<RequiredPermission>().Should().BeTrue();
    }

    [Fact]
    public void A_convention_satisfies_a_requirement()
    {
        var act = () => Build(messaging => messaging
            .DeclareConvention<RequiredPermission>(messageType => new RequiredPermission("convention." + messageType.Name))
            .RequireDeclaration<RequiredPermission>());

        // Conventions land before the requirements are checked, so the derived values count.
        act.Should().NotThrow();
    }

    [Fact]
    public void A_second_convention_for_one_value_type_is_reported()
    {
        var act = () => Build(messaging => messaging
            .DeclareConvention<RequiredPermission>(messageType => new RequiredPermission("first"))
            .DeclareConvention<RequiredPermission>(messageType => new RequiredPermission("second")));

        // One of them would have to be discarded and nothing says which.
        act.Should().Throw<PipelineContractException>()
            .WithMessage("*convention*RequiredPermission*");
    }

    [Fact]
    public void A_convention_rejects_a_null_function()
    {
        var act = () => Build(messaging => messaging.DeclareConvention<RequiredPermission>(null!));

        act.Should().Throw<ArgumentNullException>();
    }

    /// <summary>
    ///     Builds a provider over the schedule commands with the given messaging configuration.
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
                    builder.Register<PublishScheduleCommand>();
                    builder.Register<PublishScheduleCommandHandler>();
                    builder.Register<PublishScheduleCommandDefinition>();
                    builder.Register<TouchScheduleCommand>();
                    builder.Register<TouchScheduleCommandHandler>();
                    builder.Register<DraftScheduleCommand>();
                    builder.Register<DraftScheduleCommandHandler>();
                    builder.Register<BrowseScheduleCommand>();
                    builder.Register<BrowseScheduleCommandHandler>();
                    builder.Register<KeyedTransferCommand>();
                    builder.Register<KeyedTransferCommandHandler>();
                });
            })
            .BuildServiceProvider();
    }
}

/// <summary>
///     Marks a command carrying its own idempotency key.
/// </summary>
internal interface IHasIdempotencyKey
{
    /// <summary>
    ///     Gets the stable key repeated deliveries of the command share.
    /// </summary>
    string IdempotencyKey { get; }
}

/// <summary>
///     A command carrying an idempotency key but declaring no idempotency position.
/// </summary>
internal sealed class KeyedTransferCommand : ICommand, IHasIdempotencyKey
{
    /// <inheritdoc />
    public string IdempotencyKey { get; set; } = "key-1";
}

/// <summary>
///     Handles <see cref="KeyedTransferCommand" />.
/// </summary>
internal sealed class KeyedTransferCommandHandler : ICommandHandler<KeyedTransferCommand>
{
    /// <inheritdoc />
    public Task HandleAsync(KeyedTransferCommand message, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
