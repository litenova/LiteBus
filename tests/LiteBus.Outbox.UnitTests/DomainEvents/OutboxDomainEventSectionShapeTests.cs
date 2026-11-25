using LiteBus.Events.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox.Storage.InMemory;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Outbox.UnitTests.DomainEvents;

/// <summary>
///     Pins the domain-event decisions section data shape the host validation event check parses.
/// </summary>
/// <remarks>
///     The event-consistency check reads this section by name through
///     <see cref="ICompositionSectionContributor.GetData" /> without referencing the outbox package, so a shape
///     change here must fail this suite first rather than silently changing what host validation verifies.
/// </remarks>
public sealed class OutboxDomainEventSectionShapeTests
{
    /// <summary>
    ///     Confirms the decisions section reports every decided event type with its kind.
    /// </summary>
    [Fact]
    public void CompositionModel_WithDecidedEvents_ShouldReportDecisionsByTypeNameAndKind()
    {
        using var provider = new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ =>
                {
                });
                registry.AddOutbox(outbox =>
                {
                    outbox.Contracts.Register<ShapeStagedEvent>("test.shape");
                    outbox.UseInMemoryStorage();
                    outbox.StageCollectedEvents();
                    outbox.StageDomainEvent<ShapeStagedEvent>();
                    outbox.IgnoreDomainEvent<ShapeIgnoredEvent>();
                    outbox.TranslateDomainEvent<ShapeTranslatedEvent>(domainEvent =>
                        [new ShapeStagedEvent(domainEvent.OrderId)]);
                });
            })
            .BuildServiceProvider();

        var model = provider.GetRequiredService<ICompositionModel>();
        var section = model.Sections.Should().ContainSingle(contributor =>
                contributor.SectionName == "outbox-domain-events")
            .Which;

        var data = section.GetData().Should().BeAssignableTo<IDictionary<string, object?>>().Which;
        data.Should().ContainKey("undecidedPolicy");
        data["undecidedPolicy"].Should().BeOfType<string>();

        var decisions = data.Should().ContainKey("decisions").WhoseValue
            .Should().BeAssignableTo<IEnumerable<IDictionary<string, object?>>>().Which
            .ToList();

        decisions.Should().ContainSingle(decision =>
                decision["eventType"] as string == typeof(ShapeStagedEvent).FullName
                && decision["kind"] as string == "Stage");
        decisions.Should().ContainSingle(decision =>
                decision["eventType"] as string == typeof(ShapeIgnoredEvent).FullName
                && decision["kind"] as string == "Ignore");
        decisions.Should().ContainSingle(decision =>
                decision["eventType"] as string == typeof(ShapeTranslatedEvent).FullName
                && decision["kind"] as string == "Translate");
    }

    private sealed record ShapeStagedEvent(Guid OrderId) : IEvent;

    private sealed record ShapeIgnoredEvent(Guid OrderId) : IEvent;

    private sealed record ShapeTranslatedEvent(Guid OrderId) : IEvent;
}
