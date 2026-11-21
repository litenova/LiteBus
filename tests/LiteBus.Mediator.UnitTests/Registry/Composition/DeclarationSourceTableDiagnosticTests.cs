using LiteBus.Commands;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Mediator.UnitTests.Metadata;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Registry;
using LiteBus.Runtime.Abstractions.Diagnostics;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.Registry.Composition;

/// <summary>
///     Verifies that the declarations probe keeps its human text and carries the per-message declaration source
///     table in its result data.
/// </summary>
[Collection("Sequential")]
public sealed class DeclarationSourceTableDiagnosticTests : LiteBusTestBase
{
    [Fact]
    public async Task The_probe_data_carries_per_message_source_table()
    {
        var provider = Build(messaging => messaging
            .RequireDeclaration<RequiredPermission>(DeclarationEnforcement.Report));

        var result = await provider.GetRequiredService<DeclarationReportDiagnosticCheck>()
            .CheckAsync().ConfigureAwait(false);

        result.Status.Should().Be(DiagnosticStatus.Degraded);

        var sources = (SortedDictionary<string, SortedDictionary<string, string>>) result.Data!["sources"];

        sources[typeof(PublishScheduleCommand).FullName!][typeof(RequiredPermission).FullName!]
            .Should().Be("Definition");
        sources[typeof(TouchScheduleCommand).FullName!][typeof(RequiredPermission).FullName!]
            .Should().Be("Attribute");
        sources[typeof(BrowseScheduleCommand).FullName!][typeof(RequiredPermission).FullName!]
            .Should().Be("Exemption");

        // A message declaring nothing reads as an empty table rather than as a message the probe never saw.
        sources[typeof(DraftScheduleCommand).FullName!].Should().BeEmpty();
        sources[typeof(WithdrawScheduleCommand).FullName!].Should().BeEmpty();
    }

    [Fact]
    public async Task The_probe_data_carries_source_table_when_healthy()
    {
        var provider = BuildDeclared(messaging => messaging
            .RequireDeclaration<RequiredPermission>(DeclarationEnforcement.Report));

        var result = await provider.GetRequiredService<DeclarationReportDiagnosticCheck>()
            .CheckAsync().ConfigureAwait(false);

        result.Status.Should().Be(DiagnosticStatus.Healthy);

        var sources = (SortedDictionary<string, SortedDictionary<string, string>>) result.Data!["sources"];

        sources.Should().HaveCount(3);
        sources[typeof(PublishScheduleCommand).FullName!][typeof(RequiredPermission).FullName!]
            .Should().Be("Definition");
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
