using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Audit;
using LiteBus.Queries;
using LiteBus.Queries.Abstractions;
using LiteBus.Runtime.Abstractions.Diagnostics;
using LiteBus.Runtime.Abstractions.Hosting;
using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.Auditing;

/// <summary>
///     Verifies that the audit writer produces records at the mediation boundary from declarative metadata, on every
///     outcome.
/// </summary>
[Collection("Sequential")]
public sealed class AuditTrailTests : LiteBusTestBase
{
    /// <summary>
    ///     Builds a provider with auditing enabled on both the command and query axes.
    /// </summary>
    /// <param name="trail">The recording trail to register.</param>
    /// <param name="useCustomOutcomeMapper">Whether to register the refusal-aware outcome mapper.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildProvider(RecordingAuditTrail trail, bool useCustomOutcomeMapper = false)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAuditTrail>(trail);

        return services
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(messaging =>
                {
                    if (useCustomOutcomeMapper)
                    {
                        messaging.UseAuditOutcomeMapper(new TestAuditOutcomeMapper());
                    }
                });

                registry.AddCommands(builder =>
                {
                    builder.Register<PlaceOrderCommand>();
                    builder.Register<PlaceOrderCommandHandler>();
                    builder.Register<PlaceOrderCommandDefinition>();
                    builder.Register<ShipOrderCommand>();
                    builder.Register<ShipOrderCommandHandler>();
                    builder.Register<ShipOrderCommandDefinition>();
                    builder.Register<BrowseStorefrontCommand>();
                    builder.Register<BrowseStorefrontCommandHandler>();
                    builder.Register<CancelOrderCommand>();
                    builder.Register<CancelOrderCommandHandler>();
                    builder.Register<CancelOrderCommandDefinition>();
                    builder.Register<AuditableWriteDefinition>();
                    builder.Register<AdjustStockCommand>();
                    builder.Register<AdjustStockCommandHandler>();
                    builder.Register<OverridePriceCommand>();
                    builder.Register<OverridePriceCommandHandler>();
                    builder.Register<ApproveRefundCommand>();
                    builder.Register<ApproveRefundCommandGuard>();
                    builder.Register<ApproveRefundCommandHandler>();
                    builder.EnableAuditing();
                });

                registry.AddQueries(builder =>
                {
                    builder.Register<ExportOrdersQuery>();
                    builder.Register<ExportOrdersQueryHandler>();
                    builder.Register<ReadOrderQuery>();
                    builder.Register<ReadOrderQueryShortcut>();
                    builder.Register<ReadOrderQueryHandler>();
                    builder.EnableAuditing();
                });
            })
            .BuildServiceProvider();
    }

    [Fact]
    public async Task An_attribute_declared_command_produces_a_record_with_handler_supplied_detail()
    {
        var trail = new RecordingAuditTrail();
        var provider = BuildProvider(trail);

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new PlaceOrderCommand()).ConfigureAwait(false);

        var record = trail.Records.Should().ContainSingle().Subject;
        record.Action.Should().Be("orders.place-order");
        record.Outcome.Should().Be(AuditOutcome.Succeeded);
        record.Category.Should().Be("money");
        record.TargetKind.Should().Be("order");
        record.TargetId.Should().Be("order-42");
        record.Reason.Should().Be("customer requested");
        record.Properties.Should().ContainKey("channel").WhoseValue.Should().Be("web");
        record.FailureCode.Should().BeNull();
    }

    [Fact]
    public async Task A_definition_declared_command_produces_a_record()
    {
        var trail = new RecordingAuditTrail();
        var provider = BuildProvider(trail);

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new ShipOrderCommand()).ConfigureAwait(false);

        var record = trail.Records.Should().ContainSingle().Subject;
        record.Action.Should().Be("orders.ship-order");
        record.Category.Should().Be("lifecycle");
        record.TargetKind.Should().Be("shipment");
    }

    [Fact]
    public async Task A_definition_takes_precedence_over_an_attribute()
    {
        var trail = new RecordingAuditTrail();
        var provider = BuildProvider(trail);

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new CancelOrderCommand()).ConfigureAwait(false);

        trail.Records.Should().ContainSingle()
            .Which.Action.Should().Be("orders.cancel-order-from-definition");
    }

    [Fact]
    public async Task An_exempt_command_produces_no_record()
    {
        var trail = new RecordingAuditTrail();
        var provider = BuildProvider(trail);

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new BrowseStorefrontCommand()).ConfigureAwait(false);

        trail.Records.Should().BeEmpty();
    }

    [Fact]
    public async Task A_refusal_is_recorded_as_a_failure_by_default()
    {
        var trail = new RecordingAuditTrail();
        var provider = BuildProvider(trail);

        var act = async () => await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new PlaceOrderCommand { ShouldRefuse = true }).ConfigureAwait(false);

        await act.Should().ThrowAsync<ForbiddenException>().ConfigureAwait(false);

        var record = trail.Records.Should().ContainSingle().Subject;
        record.Outcome.Should().Be(AuditOutcome.Failed);
        record.FailureCode.Should().Be(nameof(ForbiddenException));
    }

    [Fact]
    public async Task A_refusal_is_recorded_as_a_denial_when_an_outcome_mapper_says_so()
    {
        var trail = new RecordingAuditTrail();
        var provider = BuildProvider(trail, useCustomOutcomeMapper: true);

        var act = async () => await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new PlaceOrderCommand { ShouldRefuse = true }).ConfigureAwait(false);

        await act.Should().ThrowAsync<ForbiddenException>().ConfigureAwait(false);

        var record = trail.Records.Should().ContainSingle().Subject;
        record.Outcome.Should().Be(AuditOutcome.Denied);
        record.Action.Should().Be("orders.place-order");
    }

    [Fact]
    public async Task An_audited_query_produces_a_record()
    {
        var trail = new RecordingAuditTrail();
        var provider = BuildProvider(trail);

        var result = await provider.GetRequiredService<IQueryMediator>()
            .QueryAsync(new ExportOrdersQuery()).ConfigureAwait(false);

        result.Should().Be("csv");

        var record = trail.Records.Should().ContainSingle().Subject;
        record.Action.Should().Be("orders.export-orders");
        record.Category.Should().Be("privacy");
    }

    [Fact]
    public void An_application_owned_declaration_is_applied_without_LiteBus_knowing_it()
    {
        var trail = new RecordingAuditTrail();
        var provider = BuildProvider(trail);

        var descriptor = provider.GetRequiredService<IMessageRegistry>().Find(typeof(PlaceOrderCommand));

        descriptor.Should().NotBeNull();
        descriptor!.Metadata.TryGet<RequiredPermission>(out var permission).Should().BeTrue();
        permission!.Name.Should().Be("orders.place");
    }

    [Fact]
    public void An_attribute_is_normalized_to_the_declaration_a_definition_would_contribute()
    {
        var trail = new RecordingAuditTrail();
        var provider = BuildProvider(trail);

        var descriptor = provider.GetRequiredService<IMessageRegistry>().Find(typeof(BrowseStorefrontCommand));

        // The attribute is not stored as itself. Both sources contribute an AuditDeclaration, which is why a definition
        // overwrites an attribute instead of sitting beside it, and why a reader needs one lookup rather than three.
        descriptor!.Metadata.TryGet<AuditDeclaration>(out var declaration).Should().BeTrue();
        declaration.Should().BeOfType<AuditExemptDeclaration>()
            .Which.Rationale.Should().Be("browsing a public storefront is not a sensitive action");

        descriptor.Metadata.Contains<AuditExemptAttribute>().Should().BeFalse();
    }

    [Fact]
    public void Attributes_that_do_not_declare_metadata_are_not_collected()
    {
        var trail = new RecordingAuditTrail();
        var provider = BuildProvider(trail);

        var descriptor = provider.GetRequiredService<IMessageRegistry>().Find(typeof(PlaceOrderCommand));

        // Message types carry attributes for serialization, diagnostics and source generators. Collecting them all
        // would make the metadata collection unbounded and would answer questions LiteBus never meant to answer.
        descriptor!.Metadata.Contains<ObsoleteAttribute>().Should().BeFalse();
        descriptor.Metadata.Values.Should().NotContain(value => value is Attribute);
    }

    [Fact]
    public async Task A_gate_denial_is_recorded_as_a_denial_without_any_outcome_mapper()
    {
        var trail = new RecordingAuditTrail();
        var provider = BuildProvider(trail);

        var act = async () => await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new ApproveRefundCommand { ShouldDeny = true }).ConfigureAwait(false);

        await act.Should().ThrowAsync<LiteBusMessageDeniedException>().ConfigureAwait(false);

        var record = trail.Records.Should().ContainSingle().Subject;
        record.Action.Should().Be("orders.approve-refund");
        record.Outcome.Should().Be(AuditOutcome.Denied);
        record.Reason.Should().Be("the approver is the requester");

        // Coding the denial with the exception type name would only restate the outcome, and would differ between a
        // refusal that throws and one that hands back a value.
        record.FailureCode.Should().BeNull();
    }

    [Fact]
    public async Task An_early_answer_is_recorded_as_a_success_rather_than_a_denial()
    {
        var trail = new RecordingAuditTrail();
        var provider = BuildProvider(trail);

        var result = await provider.GetRequiredService<IQueryMediator>()
            .QueryAsync(new ReadOrderQuery { ServeFromCache = true }).ConfigureAwait(false);

        result.Should().Be("cached-order");

        // A cache hit refused nobody. Recording it as a denial would put a false entry in the one artifact a security
        // review reads, which is why the pipeline keeps the two endings apart.
        var record = trail.Records.Should().ContainSingle().Subject;
        record.Outcome.Should().Be(AuditOutcome.Succeeded);
        record.Reason.Should().Be("served from cache");
    }

    [Fact]
    public async Task A_cancelled_mediation_still_produces_its_record()
    {
        var trail = new RecordingAuditTrail();
        var provider = BuildProvider(trail);

        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync().ConfigureAwait(false);

        var act = async () => await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new ApproveRefundCommand { ShouldCancel = true }, cancellation.Token).ConfigureAwait(false);

        await act.Should().ThrowAsync<OperationCanceledException>().ConfigureAwait(false);

        // The completion stage observes an ending that has already happened, so it is not cancellable. Handing the trail
        // the token that just fired would drop the record for every cancelled mediation.
        var record = trail.Records.Should().ContainSingle().Subject;
        record.Outcome.Should().Be(AuditOutcome.Canceled);
        record.FailureCode.Should().Contain("Canceled");
    }

    [Fact]
    public async Task A_declaration_over_a_marker_interface_covers_the_messages_beneath_it()
    {
        var trail = new RecordingAuditTrail();
        var provider = BuildProvider(trail);

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new AdjustStockCommand()).ConfigureAwait(false);

        var record = trail.Records.Should().ContainSingle().Subject;
        record.Action.Should().Be("writes.generic");
        record.Category.Should().Be("lifecycle");
    }

    [Fact]
    public async Task A_required_reason_that_the_handler_supplies_is_recorded()
    {
        var trail = new RecordingAuditTrail();
        var provider = BuildProvider(trail);

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new OverridePriceCommand { SupplyReason = true }).ConfigureAwait(false);

        trail.Records.Should().ContainSingle().Which.Reason.Should().Be("manager approved");
    }

    [Fact]
    public async Task A_required_reason_that_goes_missing_is_reported_rather_than_recorded_as_absent()
    {
        var trail = new RecordingAuditTrail();
        var provider = BuildProvider(trail);

        var act = async () => await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new OverridePriceCommand()).ConfigureAwait(false);

        // A justification that silently goes missing defeats the reason the declaration asked for one. The exception is
        // its own type rather than a configuration error: a handler that forgot a call is a data problem in one
        // mediation, and an application catching composition faults at startup must not also catch this.
        var thrown = await act.Should().ThrowAsync<AuditReasonMissingException>()
            .WithMessage("*declares that a reason is required*").ConfigureAwait(false);

        thrown.Which.Should().NotBeAssignableTo<LiteBusConfigurationException>();
        thrown.Which.MessageType.Should().Be<OverridePriceCommand>();
        thrown.Which.Action.Should().NotBeNullOrWhiteSpace();

        trail.Records.Should().BeEmpty();
    }

    [Fact]
    public void Two_definitions_declaring_the_same_value_for_one_message_are_reported_at_registration()
    {
        var act = () => new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });

                registry.AddCommands(builder =>
                {
                    builder.Register<DoubleDeclaredCommand>();
                    builder.Register<FirstDoubleDeclaration>();
                    builder.Register<SecondDoubleDeclaration>();
                });
            });

        // Definitions are applied in whatever order assembly scanning finds them, so letting the last one win would make
        // the effective audit action depend on file layout.
        act.Should().Throw<LiteBusConfigurationException>()
            .WithMessage("*FirstDoubleDeclaration*SecondDoubleDeclaration*");
    }

    [Fact]
    public async Task The_audit_probe_reports_unhealthy_when_no_trail_is_registered()
    {
        var provider = new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });
                registry.AddCommands(builder => builder.EnableAuditing());
            })
            .BuildServiceProvider();

        var manifest = provider.GetRequiredService<LiteBusHostManifest>();

        manifest.DiagnosticChecks.Should().ContainSingle(descriptor =>
            descriptor.ImplementationType == typeof(AuditTrailDiagnosticCheck));

        var diagnostics = await DiagnosticCheckRunner
            .RunAsync(manifest, provider, failHealthWhenNoProbes: true)
            .ConfigureAwait(false);

        // Without the probe, a missing trail first shows up as a fault inside the completion stage, which is the one
        // stage whose faults are deliberately kept away from the caller.
        diagnostics.Probes.Should().ContainSingle(probe => probe.Name == AuditTrailDiagnosticCheck.CheckName)
            .Which.Status.Should().Be(DiagnosticStatus.Unhealthy);
    }
}
