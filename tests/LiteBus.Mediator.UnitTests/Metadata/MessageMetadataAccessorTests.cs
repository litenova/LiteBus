using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.Metadata;

/// <summary>
///     Verifies that an application can read a message's declared metadata through
///     <see cref="IMessageMetadataAccessor" /> rather than by navigating registry descriptors.
/// </summary>
[Collection("Sequential")]
public sealed class MessageMetadataAccessorTests : LiteBusTestBase
{
    /// <summary>
    ///     Builds a provider registering the declaration test types without enabling auditing.
    /// </summary>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();

        return services
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });

                registry.AddCommands(builder =>
                {
                    builder.Register<PublishScheduleCommand>();
                    builder.Register<PublishScheduleCommandHandler>();
                    builder.Register<PublishScheduleCommandDefinition>();
                    builder.Register<TouchScheduleCommand>();
                    builder.Register<TouchScheduleCommandHandler>();
                });
            })
            .BuildServiceProvider();
    }

    [Fact]
    public void A_declared_value_is_readable_by_its_own_type()
    {
        var accessor = BuildProvider().GetRequiredService<IMessageMetadataAccessor>();

        accessor.TryGet<PublishScheduleCommand, RequiredPermission>(out var required).Should().BeTrue();
        required!.Name.Should().Be("schedules.publish");
    }

    [Fact]
    public void An_attribute_declaration_is_readable_the_same_way_as_a_definition()
    {
        var accessor = BuildProvider().GetRequiredService<IMessageMetadataAccessor>();

        // The attribute implements IMessageDeclarationSource, so it lands under the same key a definition would use.
        accessor.TryGet<TouchScheduleCommand, RequiredPermission>(out var required).Should().BeTrue();
        required!.Name.Should().Be("schedules.touch");
    }

    [Fact]
    public void A_message_declaring_nothing_reports_absence_rather_than_failing()
    {
        var accessor = BuildProvider().GetRequiredService<IMessageMetadataAccessor>();

        accessor.TryGet<PublishScheduleCommand, RetentionClass>(out var retention).Should().BeFalse();
        retention.Should().BeNull();
        accessor.ForMessage<PublishScheduleCommand>().Contains<RetentionClass>().Should().BeFalse();
    }

    [Fact]
    public void An_unregistered_type_is_reported_instead_of_answered_with_nothing()
    {
        var accessor = BuildProvider().GetRequiredService<IMessageMetadataAccessor>();

        // Answering an unregistered type with an empty bag would let a permission guard pass a message nobody
        // registered, which is the one failure mode worth an exception.
        var act = () => accessor.ForMessage(typeof(string));

        act.Should().Throw<MessageMetadataNotFoundException>()
            .Which.MessageType.Should().Be<string>();
    }

    [Fact]
    public void The_accessor_reads_audit_declarations_without_auditing_being_enabled()
    {
        var accessor = BuildProvider().GetRequiredService<IMessageMetadataAccessor>();

        accessor.TryGet<PublishScheduleCommand, AuditDeclaration>(out var declaration).Should().BeTrue();
        declaration.Should().BeOfType<AuditedDeclaration>()
            .Which.Action.Should().Be("schedules.publish");
    }

    [Fact]
    public async Task A_generic_guard_enforces_the_declaration_it_reads()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentActor>(new FakeActor("schedules.touch"));

        var provider = services
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });

                registry.AddCommands(builder =>
                {
                    builder.Register<PublishScheduleCommand>();
                    builder.Register<PublishScheduleCommandHandler>();
                    builder.Register<PublishScheduleCommandDefinition>();
                    builder.Register<TouchScheduleCommand>();
                    builder.Register<TouchScheduleCommandHandler>();
                    builder.Register(typeof(PermissionGuard<>));
                });
            })
            .BuildServiceProvider();

        var mediator = provider.GetRequiredService<ICommandMediator>();

        // The actor holds the permission this command declares.
        await mediator.SendAsync(new TouchScheduleCommand()).ConfigureAwait(false);

        // One guard covers every declaring message, so this one is refused without a guard of its own.
        var act = async () => await mediator.SendAsync(new PublishScheduleCommand()).ConfigureAwait(false);

        await act.Should().ThrowAsync<LiteBusMessageDeniedException>().ConfigureAwait(false);
    }
}
