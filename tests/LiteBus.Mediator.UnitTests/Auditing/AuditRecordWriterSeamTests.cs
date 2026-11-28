using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Auditing;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Auditing.Recording;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Diagnostics;
using LiteBus.Runtime.Abstractions.Hosting;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;
using LiteBus.Auditing.Abstractions;

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
            .AddLiteBus(registry =>
            {
                registry.AddAuditing(auditing => auditing
                    .UseTrailInstance(trail)
                    .UseRecordWriterInstance(writer)
                    .ForCommands());

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
                registry.AddAuditing(auditing => auditing
                    .UseTrailInstance(new RecordingAuditTrail())
                    .UseRecordWriter<CountingAuditRecordWriter>(InstanceLifetime.Singleton)
                    .ForCommands());

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
    public async Task The_probe_reports_the_writer_instead_of_assessing_the_trail()
    {
        var provider = new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddAuditing(auditing => auditing
                    .UseTrailInstance(new RecordingAuditTrail())
                    .UseRecordWriter<CountingAuditRecordWriter>(InstanceLifetime.Singleton)
                    .ForCommands());

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

        // The trail has no actor resolver, which makes the built-in writer degraded. A writer LiteBus did not build
        // may never touch a trail or an actor resolver, so the probe names that writer instead of assessing them.
        audit.Status.Should().Be(DiagnosticStatus.Healthy);
        audit.Data!["recordWriter"].Should().Be("CountingAuditRecordWriter (Singleton)");
    }

    [Fact]
    public void A_replacement_writer_is_resolved_instead_of_the_framework_default()
    {
        var provider = new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddAuditing(auditing => auditing
                    .UseTrailInstance(new RecordingAuditTrail())
                    .UseRecordWriter<CountingAuditRecordWriter>(InstanceLifetime.Singleton)
                    .ForCommands());

                registry.AddCommands(builder =>
                {
                    builder.Register<ShipOrderCommand>();
                    builder.Register<ShipOrderCommandHandler>();
                });
            })
            .BuildServiceProvider();

        provider.GetRequiredService<IAuditRecordWriter>().Should().BeOfType<CountingAuditRecordWriter>();

        // The composition model reports only framework defaults and replacements of known ports. A writer LiteBus
        // did not build leaves no default entry, because there is no default product left to name.
        provider.GetRequiredService<ICompositionModel>().Ports
            .Should().NotContain(port => port.PortType == typeof(IAuditRecordWriter));
    }

    [Fact]
    public void The_built_in_writer_is_the_one_the_container_resolves()
    {
        var provider = new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddAuditing(auditing => auditing
                    .UseTrailInstance(new RecordingAuditTrail())
                    .ForCommands());

                registry.AddCommands(builder =>
                {
                    builder.Register<ShipOrderCommand>();
                    builder.Register<ShipOrderCommandHandler>();
                });
            })
            .BuildServiceProvider();

        provider.GetRequiredService<IAuditRecordWriter>().Should().BeOfType<AuditRecordWriter>();

        provider.GetRequiredService<ICompositionModel>().Ports
            .Should().ContainSingle(port => port.PortType == typeof(IAuditRecordWriter))
            .Which.ImplementationType.Should().Be<AuditRecordWriter>();
    }
}
