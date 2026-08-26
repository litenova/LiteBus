using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Queries;
using LiteBus.Queries.Abstractions;
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
                    builder.EnableAuditing();
                });

                registry.AddQueries(builder =>
                {
                    builder.Register<ExportOrdersQuery>();
                    builder.Register<ExportOrdersQueryHandler>();
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
    public void An_application_owned_facet_is_applied_without_LiteBus_knowing_it()
    {
        var trail = new RecordingAuditTrail();
        var provider = BuildProvider(trail);

        var descriptor = provider.GetRequiredService<IMessageRegistry>().Find(typeof(PlaceOrderCommand));

        descriptor.Should().NotBeNull();
        descriptor!.Metadata.TryGet<RequiredPermission>(out var permission).Should().BeTrue();
        permission!.Name.Should().Be("orders.place");
    }

    [Fact]
    public void Attributes_are_exposed_as_message_metadata()
    {
        var trail = new RecordingAuditTrail();
        var provider = BuildProvider(trail);

        var descriptor = provider.GetRequiredService<IMessageRegistry>().Find(typeof(BrowseStorefrontCommand));

        descriptor!.Metadata.TryGet<AuditExemptAttribute>(out var exempt).Should().BeTrue();
        exempt!.Rationale.Should().Be("browsing a public storefront is not a sensitive action");
    }
}
