using LiteBus.Commands;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Registry;
using LiteBus.Runtime.Abstractions.Diagnostics;
using LiteBus.Runtime.Abstractions.Hosting;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.Metadata;

/// <summary>
///     Verifies that a report-mode requirement lists undeclared messages through the declarations probe instead of
///     failing composition.
/// </summary>
/// <remarks>
///     A failing requirement can only land atomically with the last declaration, because the first missing position
///     fails the whole composition. Reporting the gap as degraded lets a team adopt positions message by message,
///     which is the gradual path these tests pin.
/// </remarks>
[Collection("Sequential")]
public sealed class DeclarationReportModeTests : LiteBusTestBase
{
    [Fact]
    public void Report_mode_does_not_fail_composition()
    {
        var act = () => Build(messaging => messaging
            .RequireDeclaration<RequiredPermission>(DeclarationEnforcement.Report));

        act.Should().NotThrow();
    }

    [Fact]
    public void Report_mode_registers_the_declarations_probe()
    {
        var provider = Build(messaging => messaging
            .RequireDeclaration<RequiredPermission>(DeclarationEnforcement.Report));

        provider.GetRequiredService<LiteBusHostManifest>().DiagnosticChecks
            .Count(descriptor =>
                descriptor.ImplementationType == typeof(DeclarationReportDiagnosticCheck)
                && descriptor.Name == DeclarationReportDiagnosticCheck.CheckName)
            .Should().Be(1);
        provider.GetRequiredService<DeclarationReportDiagnosticCheck>().Name.Should().Be("litebus.declarations");
    }

    [Fact]
    public async Task The_probe_reports_degraded_listing_every_undeclared_message()
    {
        var provider = Build(messaging => messaging
            .RequireDeclaration<RequiredPermission>(DeclarationEnforcement.Report));

        var result = await provider.GetRequiredService<DeclarationReportDiagnosticCheck>()
            .CheckAsync().ConfigureAwait(false);

        result.Status.Should().Be(DiagnosticStatus.Degraded);
        result.Description.Should().Contain("DraftScheduleCommand").And.Contain("WithdrawScheduleCommand");
        result.Description.Should().NotContain("BrowseScheduleCommand");
        result.Description.Should().NotContain("PublishScheduleCommand");
        result.Description.Should().NotContain("TouchScheduleCommand");
        result.Data.Should().NotBeNull();
        result.Data!["undeclaredCount"].Should().Be(2);
    }

    [Fact]
    public async Task The_probe_reports_healthy_when_every_message_states_a_position()
    {
        var provider = BuildDeclared(messaging => messaging
            .RequireDeclaration<RequiredPermission>(DeclarationEnforcement.Report));

        var result = await provider.GetRequiredService<DeclarationReportDiagnosticCheck>()
            .CheckAsync().ConfigureAwait(false);

        result.Status.Should().Be(DiagnosticStatus.Healthy);
    }

    [Fact]
    public void The_catalog_lists_the_messages_missing_a_reported_declaration()
    {
        var catalog = Build(messaging => messaging
                .RequireDeclaration<RequiredPermission>(DeclarationEnforcement.Report))
            .GetRequiredService<IMessageCatalog>();

        catalog.MissingDeclaration(typeof(RequiredPermission))
            .Select(entry => entry.MessageType.Name)
            .Should().BeEquivalentTo("DraftScheduleCommand", "WithdrawScheduleCommand");
    }

    [Fact]
    public void The_catalog_limits_the_report_to_the_requirement_scope()
    {
        var catalog = Build(messaging => messaging
                .RequireDeclaration<RequiredPermission>(DeclarationEnforcement.Report))
            .GetRequiredService<IMessageCatalog>();

        var missing = catalog.MissingDeclaration(
            typeof(RequiredPermission),
            messageType => messageType == typeof(DraftScheduleCommand));

        missing.Should().ContainSingle().Which.MessageType.Should().Be<DraftScheduleCommand>();
    }

    [Fact]
    public void The_catalog_rejects_a_null_value_type()
    {
        var catalog = Build(messaging => messaging
                .RequireDeclaration<RequiredPermission>(DeclarationEnforcement.Report))
            .GetRequiredService<IMessageCatalog>();

        var act = () => catalog.MissingDeclaration(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task A_satisfied_fail_requirement_and_an_unsatisfied_report_requirement_compose_with_a_degraded_probe()
    {
        var provider = Build(messaging => messaging
            .RequireDeclaration<RequiredPermission, PublishScheduleCommand>()
            .RequireDeclaration<RequiredPermission>(DeclarationEnforcement.Report));

        // The fail-mode requirement holds, so composition succeeds, and the report-mode one still lists its gap.
        var result = await provider.GetRequiredService<DeclarationReportDiagnosticCheck>()
            .CheckAsync().ConfigureAwait(false);

        result.Status.Should().Be(DiagnosticStatus.Degraded);
        result.Description.Should().Contain("DraftScheduleCommand").And.Contain("WithdrawScheduleCommand");
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
                    builder.Register<WithdrawScheduleCommand>();
                    builder.Register<WithdrawScheduleCommandHandler>();
                    builder.Register<BrowseScheduleCommand>();
                    builder.Register<BrowseScheduleCommandHandler>();
                });
            })
            .BuildServiceProvider();
    }

    /// <summary>
    ///     Builds a provider over only the schedule commands that state a permission position.
    /// </summary>
    /// <param name="messaging">The messaging configuration under test.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildDeclared(Action<MessageModuleBuilder> messaging)
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
                    builder.Register<BrowseScheduleCommand>();
                    builder.Register<BrowseScheduleCommandHandler>();
                });
            })
            .BuildServiceProvider();
    }
}
