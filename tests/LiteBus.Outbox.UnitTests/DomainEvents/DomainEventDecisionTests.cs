using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.DomainEvents;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Outbox.UnitTests.DomainEvents;

/// <summary>
///     Verifies the outbox builder accepts one decision per domain event type, treats an identical repeat as a no-op, and fails composition for a conflicting repeat naming both decisions.
/// </summary>
public sealed class DomainEventDecisionTests
{
    /// <summary>
    ///     Verifies translating then ignoring the same event type fails composition naming both decisions.
    /// </summary>
    [Fact]
    public void Translate_then_Ignore_same_event_should_throw_naming_both_decisions()
    {
        var builder = new OutboxModuleBuilder();
        builder.TranslateDomainEvent<DecisionEvent>(static _ => []);

        var act = () => builder.IgnoreDomainEvent<DecisionEvent>();

        act.Should().Throw<LiteBusConfigurationException>()
            .WithMessage("*DecisionEvent*TranslateDomainEvent*IgnoreDomainEvent*");
    }

    /// <summary>
    ///     Verifies ignoring then translating the same event type fails composition naming both decisions.
    /// </summary>
    [Fact]
    public void Ignore_then_Translate_same_event_should_throw_naming_both_decisions()
    {
        var builder = new OutboxModuleBuilder();
        builder.IgnoreDomainEvent<DecisionEvent>();

        var act = () => builder.TranslateDomainEvent<DecisionEvent>(static _ => []);

        act.Should().Throw<LiteBusConfigurationException>()
            .WithMessage("*DecisionEvent*IgnoreDomainEvent*TranslateDomainEvent*");
    }

    /// <summary>
    ///     Verifies translating then publishing the same event type in process fails composition naming both decisions.
    /// </summary>
    [Fact]
    public void Translate_then_PublishInProcess_same_event_should_throw_naming_both_decisions()
    {
        var builder = new OutboxModuleBuilder();
        builder.TranslateDomainEvent<DecisionEvent>(static _ => []);

        var act = () => builder.PublishDomainEventInProcess<DecisionEvent>();

        act.Should().Throw<LiteBusConfigurationException>()
            .WithMessage("*DecisionEvent*TranslateDomainEvent*PublishDomainEventInProcess*");
    }

    /// <summary>
    ///     Verifies publishing then ignoring the same event type in process fails composition naming both decisions.
    /// </summary>
    [Fact]
    public void PublishInProcess_then_Ignore_same_event_should_throw_naming_both_decisions()
    {
        var builder = new OutboxModuleBuilder();
        builder.PublishDomainEventInProcess<DecisionEvent>();

        var act = () => builder.IgnoreDomainEvent<DecisionEvent>();

        act.Should().Throw<LiteBusConfigurationException>()
            .WithMessage("*DecisionEvent*PublishDomainEventInProcess*IgnoreDomainEvent*");
    }

    /// <summary>
    ///     Verifies a function translation and a translator type for the same event type conflict.
    /// </summary>
    [Fact]
    public void Translate_function_then_translator_type_should_throw()
    {
        var builder = new OutboxModuleBuilder();
        builder.TranslateDomainEvent<DecisionEvent>(static _ => []);

        var act = () => builder.TranslateDomainEvent<DecisionEvent, DecisionTranslator>();

        act.Should().Throw<LiteBusConfigurationException>()
            .WithMessage("*DecisionEvent*TranslateDomainEvent*");
    }

    /// <summary>
    ///     Verifies two translator types for the same event type conflict.
    /// </summary>
    [Fact]
    public void Translate_two_translator_types_should_throw()
    {
        var builder = new OutboxModuleBuilder();
        builder.TranslateDomainEvent<DecisionEvent, DecisionTranslator>();

        var act = () => builder.TranslateDomainEvent<DecisionEvent, OtherDecisionTranslator>();

        act.Should().Throw<LiteBusConfigurationException>()
            .WithMessage("*DecisionEvent*DecisionTranslator*OtherDecisionTranslator*");
    }

    /// <summary>
    ///     Verifies registering the same translation function twice is a no-op.
    /// </summary>
    [Fact]
    public void Translate_same_function_twice_should_be_a_noop()
    {
        var builder = new OutboxModuleBuilder();
        Func<DecisionEvent, IReadOnlyList<object>> translate = static _ => [];
        builder.TranslateDomainEvent(translate);
        builder.TranslateDomainEvent(translate);

        builder.CollectDomainEventDecisions()
            .TryGetTranslator(typeof(DecisionEvent), out _).Should().BeTrue();
    }

    /// <summary>
    ///     Verifies two different translation function instances conflict even when their bodies match.
    /// </summary>
    [Fact]
    public void Translate_different_function_instances_should_throw()
    {
        var builder = new OutboxModuleBuilder();
        builder.TranslateDomainEvent<DecisionEvent>(static _ => []);

        var act = () => builder.TranslateDomainEvent<DecisionEvent>(static _ => []);

        act.Should().Throw<LiteBusConfigurationException>()
            .WithMessage("*DecisionEvent*TranslateDomainEvent*");
    }

    /// <summary>
    ///     Verifies registering the same translator type twice is a no-op.
    /// </summary>
    [Fact]
    public void Translate_same_translator_type_twice_should_be_a_noop()
    {
        var builder = new OutboxModuleBuilder();
        builder.TranslateDomainEvent<DecisionEvent, DecisionTranslator>();
        builder.TranslateDomainEvent<DecisionEvent, DecisionTranslator>();

        var decisions = builder.CollectDomainEventDecisions();

        decisions.TryGetTranslator(typeof(DecisionEvent), out var registration).Should().BeTrue();
        registration!.TranslatorType.Should().Be<DecisionTranslator>();
    }

    /// <summary>
    ///     Verifies ignoring the same event type twice is a no-op.
    /// </summary>
    [Fact]
    public void Ignore_twice_should_be_a_noop()
    {
        var builder = new OutboxModuleBuilder();
        builder.IgnoreDomainEvent<DecisionEvent>();
        builder.IgnoreDomainEvent<DecisionEvent>();

        builder.CollectDomainEventDecisions().IsIgnored(typeof(DecisionEvent)).Should().BeTrue();
    }

    /// <summary>
    ///     Verifies publishing the same event type in process twice is a no-op.
    /// </summary>
    [Fact]
    public void PublishInProcess_twice_should_be_a_noop()
    {
        var builder = new OutboxModuleBuilder();
        builder.PublishDomainEventInProcess<DecisionEvent>();
        builder.PublishDomainEventInProcess<DecisionEvent>();

        builder.CollectDomainEventDecisions().IsPublishedInProcess(typeof(DecisionEvent)).Should().BeTrue();
    }

    /// <summary>
    ///     Verifies the undecided policy stages events as-is unless required otherwise.
    /// </summary>
    [Fact]
    public void Default_undecided_policy_should_stage_events_as_is()
    {
        var builder = new OutboxModuleBuilder();

        builder.CollectDomainEventDecisions().UndecidedPolicy.Should().Be(UndecidedDomainEventPolicy.StageAsIs);
    }

    /// <summary>
    ///     Verifies requiring a decision switches the undecided policy to throwing.
    /// </summary>
    [Fact]
    public void RequireDomainEventDecision_should_set_the_throw_policy()
    {
        var builder = new OutboxModuleBuilder();
        builder.RequireDomainEventDecision();

        builder.CollectDomainEventDecisions().UndecidedPolicy.Should().Be(UndecidedDomainEventPolicy.Throw);
    }

    /// <summary>
    ///     Verifies collected translator types hold only the translator overload registrations, not function registrations.
    /// </summary>
    [Fact]
    public void CollectDomainEventTranslatorTypes_should_return_only_translator_type_registrations()
    {
        var builder = new OutboxModuleBuilder();
        builder.TranslateDomainEvent<DecisionEvent, DecisionTranslator>();
        builder.TranslateDomainEvent<OtherDecisionEvent>(static _ => []);

        builder.CollectDomainEventTranslatorTypes().Should().Equal(typeof(DecisionTranslator));
    }

    /// <summary>
    ///     Verifies an open generic definition is rejected as a decision event type.
    /// </summary>
    [Fact]
    public void Open_event_type_should_be_rejected_as_a_decision_event_type()
    {
        var act = () => DomainEventTranslatorRegistration.ThrowIfOpenEventType(typeof(OpenDecisionEvent<>));

        act.Should().Throw<ArgumentException>()
            .WithMessage("*closed event type*");
    }

    /// <summary>
    ///     Verifies a closed event type passes the decision event type check unchanged.
    /// </summary>
    [Fact]
    public void Closed_event_type_should_pass_the_decision_event_type_check()
    {
        DomainEventTranslatorRegistration.ThrowIfOpenEventType(typeof(DecisionEvent))
            .Should().Be<DecisionEvent>();
    }

    /// <summary>
    ///     Verifies registering the same aggregate type twice through the type overload replaces the previous pair.
    /// </summary>
    [Fact]
    public void HarvestDomainEvents_type_overload_should_replace_the_accessor_on_repeat_registration()
    {
        var builder = new OutboxModuleBuilder();
#pragma warning disable CA2263 // This test covers the non-generic overload on purpose.
        builder.HarvestDomainEvents(
            typeof(DecisionAggregate),
            static aggregate => ((DecisionAggregate) aggregate).PendingEvents,
            static aggregate => ((DecisionAggregate) aggregate).PendingEvents.Clear());
        builder.HarvestDomainEvents(
            typeof(DecisionAggregate),
            static aggregate => ((DecisionAggregate) aggregate).PendingEvents,
            static _ => { });
#pragma warning restore CA2263

        builder.CollectDomainEventAccessors().Should().HaveCount(1);
    }

    /// <summary>
    ///     Verifies the generic and type harvest overloads replace each other for the same closed type.
    /// </summary>
    [Fact]
    public void HarvestDomainEvents_generic_and_type_overloads_should_replace_each_other()
    {
        var builder = new OutboxModuleBuilder();
        builder.HarvestDomainEvents<DecisionAggregate>(
            static aggregate => aggregate.PendingEvents,
            static aggregate => aggregate.PendingEvents.Clear());
#pragma warning disable CA2263 // This test covers the non-generic overload on purpose.
        builder.HarvestDomainEvents(
            typeof(DecisionAggregate),
            static aggregate => ((DecisionAggregate) aggregate).PendingEvents,
            static _ => { });
#pragma warning restore CA2263

        builder.CollectDomainEventAccessors().Should().HaveCount(1);
    }

    /// <summary>
    ///     Verifies the type harvest overload rejects value types.
    /// </summary>
    [Fact]
    public void HarvestDomainEvents_type_overload_should_reject_value_types()
    {
        var builder = new OutboxModuleBuilder();

        var act = () => builder.HarvestDomainEvents(
            typeof(int),
            static _ => [],
            static _ => { });

        act.Should().Throw<ArgumentException>()
            .WithMessage("*class or interface*");
    }

    /// <summary>
    ///     Verifies the type harvest overload rejects null arguments.
    /// </summary>
    [Fact]
    public void HarvestDomainEvents_type_overload_should_reject_null_arguments()
    {
        var builder = new OutboxModuleBuilder();

        var actType = () => builder.HarvestDomainEvents(null!, static _ => [], static _ => { });
        var actEvents = () => builder.HarvestDomainEvents(typeof(DecisionAggregate), null!, static _ => { });
        var actClear = () => builder.HarvestDomainEvents(typeof(DecisionAggregate), static _ => [], null!);

        actType.Should().Throw<ArgumentNullException>();
        actEvents.Should().Throw<ArgumentNullException>();
        actClear.Should().Throw<ArgumentNullException>();
    }

    /// <summary>
    ///     Verifies different predicates accumulate while the same predicate instance replaces its pair.
    /// </summary>
    [Fact]
    public void HarvestDomainEventsWhere_should_accumulate_predicates_and_replace_repeats()
    {
        var builder = new OutboxModuleBuilder();
        Func<Type, bool> first = static type => type.Name.StartsWith("Decision", StringComparison.Ordinal);
        builder.HarvestDomainEventsWhere(first, static _ => [], static _ => { });
        builder.HarvestDomainEventsWhere(static type => type.Name.EndsWith("Aggregate", StringComparison.Ordinal), static _ => [], static _ => { });

        builder.CollectDomainEventAccessors().Should().HaveCount(2);

        builder.HarvestDomainEventsWhere(first, static _ => [], static _ => { });

        builder.CollectDomainEventAccessors().Should().HaveCount(2);
    }

    /// <summary>
    ///     Verifies the predicate harvest overload rejects null arguments.
    /// </summary>
    [Fact]
    public void HarvestDomainEventsWhere_should_reject_null_arguments()
    {
        var builder = new OutboxModuleBuilder();

        var actPredicate = () => builder.HarvestDomainEventsWhere(null!, static _ => [], static _ => { });
        var actEvents = () => builder.HarvestDomainEventsWhere(static _ => true, null!, static _ => { });
        var actClear = () => builder.HarvestDomainEventsWhere(static _ => true, static _ => [], null!);

        actPredicate.Should().Throw<ArgumentNullException>();
        actEvents.Should().Throw<ArgumentNullException>();
        actClear.Should().Throw<ArgumentNullException>();
    }

    /// <summary>
    ///     A domain event used to register decisions.
    /// </summary>
    private sealed record DecisionEvent;

    /// <summary>
    ///     Another domain event used to register decisions.
    /// </summary>
    private sealed record OtherDecisionEvent;

    /// <summary>
    ///     An open generic domain event shape used to prove open types are rejected.
    /// </summary>
    /// <typeparam name="TId">The identifier type closing the event.</typeparam>
    private sealed record OpenDecisionEvent<TId>;

    /// <summary>
    ///     A translator used to register translator type decisions.
    /// </summary>
    private sealed class DecisionTranslator : IDomainEventTranslator<DecisionEvent>
    {
        /// <inheritdoc />
        public ValueTask<IReadOnlyList<object>> TranslateAsync(
            DecisionEvent domainEvent,
            DomainEventTranslationContext context,
            CancellationToken cancellationToken)
        {
            return new ValueTask<IReadOnlyList<object>>([]);
        }
    }

    /// <summary>
    ///     Another translator for the same event, used to prove translator conflicts.
    /// </summary>
    private sealed class OtherDecisionTranslator : IDomainEventTranslator<DecisionEvent>
    {
        /// <inheritdoc />
        public ValueTask<IReadOnlyList<object>> TranslateAsync(
            DecisionEvent domainEvent,
            DomainEventTranslationContext context,
            CancellationToken cancellationToken)
        {
            return new ValueTask<IReadOnlyList<object>>([]);
        }
    }

    /// <summary>
    ///     An aggregate used to register harvest accessors.
    /// </summary>
    private sealed class DecisionAggregate
    {
        /// <summary>
        ///     Gets the buffered domain events.
        /// </summary>
        public List<object> PendingEvents { get; } = [];
    }
}
