using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Audit;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Diagnostics;
using LiteBus.Runtime.Abstractions.Hosting;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.Auditing;

/// <summary>
///     Verifies that an application can own the audit record shape by replacing the writer.
/// </summary>
/// <remarks>
///     <see cref="AuditRecord" /> is a handoff rather than a schema, so a different set of fields needs no new
///     abstraction: it needs a different <see cref="IAuditRecordWriter" />. These tests hold the seam to what the
///     documentation promises, including that the pipeline integration a replacement keeps is real and that the
///     diagnostics stop asserting things LiteBus no longer owns.
/// </remarks>
[Collection("Sequential")]
public sealed class AuditRecordWriterSeamTests : LiteBusTestBase
{
    [Fact]
    public async Task A_replacement_writer_receives_the_completion_and_the_built_in_one_does_not_run()
    {
        var trail = new RecordingAuditTrail();
        var writer = new ShapeOfItsOwnAuditRecordWriter();

        var provider = new ServiceCollection()
            .AddSingleton<IAuditTrail>(trail)
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(messaging => messaging.AddAuditing(auditing => auditing
                    .UseRecordWriterInstance(writer)
                    .ForCommands()));

                registry.AddCommands(builder =>
                {
                    builder.Register<ShipOrderCommand>();
                    builder.Register<ShipOrderCommandHandler>();
                    builder.Register<ShipOrderCommandDefinition>();
                });
            })
            .BuildServiceProvider();

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new ShipOrderCommand()).ConfigureAwait(false);

        writer.Completions.Should().ContainSingle()
            .Which.Message.Should().BeOfType<ShipOrderCommand>();

        // The trail is still registered, and nothing wrote to it: the replacement owns the whole of record building,
        // so an application whose store is not an IAuditTrail is not forced to route through one.
        trail.Records.Should().BeEmpty();
    }

    [Fact]
    public async Task A_replacement_writer_registered_by_type_is_resolved_on_the_selected_axis()
    {
        var provider = new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(messaging => messaging.AddAuditing(auditing => auditing
                    .UseRecordWriter<CountingAuditRecordWriter>(InstanceLifetime.Singleton)
                    .ForCommands()));

                registry.AddCommands(builder =>
                {
                    builder.Register<ShipOrderCommand>();
                    builder.Register<ShipOrderCommandHandler>();
                    builder.Register<ShipOrderCommandDefinition>();
                });
            })
            .BuildServiceProvider();

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new ShipOrderCommand()).ConfigureAwait(false);

        // Registered by type with a lifetime, resolved by the completion handler on the axis that was selected.
        provider.GetRequiredService<IAuditRecordWriter>()
            .Should().BeOfType<CountingAuditRecordWriter>()
            .Which.Count.Should().Be(1);
    }

    [Fact]
    public async Task The_probe_reports_the_writer_instead_of_demanding_a_trail()
    {
        var provider = new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(messaging => messaging.AddAuditing(auditing => auditing
                    .UseRecordWriter<CountingAuditRecordWriter>(InstanceLifetime.Singleton)
                    .ForCommands()));

                registry.AddCommands(builder =>
                {
                    builder.Register<ShipOrderCommand>();
                    builder.Register<ShipOrderCommandHandler>();
                });
            })
            .BuildServiceProvider();

        var diagnostics = await DiagnosticCheckRunner
            .RunAsync(provider.GetRequiredService<LiteBusHostManifest>(), provider, failHealthWhenNoProbes: true)
            .ConfigureAwait(false);

        var audit = diagnostics.Probes
            .Should().ContainSingle(probe => probe.Name == AuditTrailDiagnosticCheck.CheckName).Subject;

        // No IAuditTrail is registered anywhere, which for the built-in writer is unhealthy and correct. A writer
        // LiteBus did not build may never touch a trail, so demanding one reports a working configuration as broken.
        audit.Status.Should().Be(DiagnosticStatus.Healthy);
        audit.Data!["recordWriter"].Should().Be("CountingAuditRecordWriter (Singleton)");
    }

    [Fact]
    public void The_summary_names_the_writer_that_replaced_the_built_in_one()
    {
        var provider = new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(messaging => messaging.AddAuditing(auditing => auditing
                    .UseRecordWriter<CountingAuditRecordWriter>(InstanceLifetime.Singleton)
                    .ForCommands()));

                registry.AddCommands(builder =>
                {
                    builder.Register<ShipOrderCommand>();
                    builder.Register<ShipOrderCommandHandler>();
                });
            })
            .BuildServiceProvider();

        var summary = provider.GetRequiredService<LiteBusCompositionSummary>();

        summary.AuditRecordWriter.Should().Be("CountingAuditRecordWriter (Singleton)");
        summary.ToString().Should().Contain("record writer CountingAuditRecordWriter (Singleton)");
    }

    [Fact]
    public void The_summary_says_nothing_about_the_writer_when_the_built_in_one_is_in_use()
    {
        var provider = new ServiceCollection()
            .AddSingleton<IAuditTrail>(new RecordingAuditTrail())
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(messaging => messaging.AddAuditing(auditing => auditing.ForCommands()));

                registry.AddCommands(builder =>
                {
                    builder.Register<ShipOrderCommand>();
                    builder.Register<ShipOrderCommandHandler>();
                });
            })
            .BuildServiceProvider();

        var summary = provider.GetRequiredService<LiteBusCompositionSummary>();

        summary.AuditRecordWriter.Should().BeNull();
        summary.ToString().Should().NotContain("record writer");
    }
}
