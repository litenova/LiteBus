using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Events;
using LiteBus.Events.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.DomainEvents;
using LiteBus.Outbox.Storage.InMemory;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Outbox.UnitTests.DomainEvents;

/// <summary>
///     Verifies drained domain events translate, drop, or stage as-is by exact runtime type, and that decision faults throw before the commit.
/// </summary>
[Collection("Sequential")]
public sealed class DomainEventTranslationTests : LiteBusTestBase
{
    /// <summary>
    ///     Verifies a function translation stages the translated message instead of the original event.
    /// </summary>
    [Fact]
    public async Task Function_translation_should_stage_the_translated_message_instead_of_the_original()
    {
        var script = new CollectScript();
        var orderId = Guid.NewGuid();
        script.Events.Add(new OrderPlacedDomainEvent { OrderId = orderId });
        var decisions = new DomainEventDecisions(
            new Dictionary<Type, DomainEventTranslatorRegistration>
            {
                [typeof(OrderPlacedDomainEvent)] = DomainEventTranslatorRegistration.ForFunction<OrderPlacedDomainEvent>(
                    static placed => [new OrderSubmittedIntegrationEvent { OrderId = placed.OrderId }])
            },
            [],
            [],
            UndecidedDomainEventPolicy.StageAsIs);
        var provider = BuildEventProvider(decisions, script);

        await provider.GetRequiredService<IEventMediator>()
            .PublishAsync(new TranslateTrigger()).ConfigureAwait(false);

        var manager = provider.GetRequiredService<IOutboxManager>();
        var submitted = await manager.QueryAsync(
            new OutboxMessageFilter { ContractName = "tests.events.order-submitted" },
            new OutboxMessagePageRequest { PageSize = 10 }).ConfigureAwait(false);
        var placed = await manager.QueryAsync(
            new OutboxMessageFilter { ContractName = "tests.events.order-placed" },
            new OutboxMessagePageRequest { PageSize = 10 }).ConfigureAwait(false);

        submitted.Items.Should().HaveCount(1);
        placed.Items.Should().BeEmpty();
    }

    /// <summary>
    ///     Verifies a translation to an empty list stages nothing.
    /// </summary>
    [Fact]
    public async Task Function_translation_to_empty_should_stage_nothing()
    {
        var script = new CollectScript();
        script.Events.Add(new OrderPlacedDomainEvent { OrderId = Guid.NewGuid() });
        var decisions = new DomainEventDecisions(
            new Dictionary<Type, DomainEventTranslatorRegistration>
            {
                [typeof(OrderPlacedDomainEvent)] = DomainEventTranslatorRegistration.ForFunction<OrderPlacedDomainEvent>(static _ => [])
            },
            [],
            [],
            UndecidedDomainEventPolicy.StageAsIs);
        var provider = BuildEventProvider(decisions, script);

        await provider.GetRequiredService<IEventMediator>()
            .PublishAsync(new TranslateTrigger()).ConfigureAwait(false);

        var manager = provider.GetRequiredService<IOutboxManager>();
        var page = await manager.QueryAsync(
            new OutboxMessageFilter(),
            new OutboxMessagePageRequest { PageSize = 10 }).ConfigureAwait(false);

        page.Items.Should().BeEmpty();
    }

    /// <summary>
    ///     Verifies a translator type resolves scoped services and observes the translation context.
    /// </summary>
    [Fact]
    public async Task Translator_type_should_resolve_scoped_services_and_observe_the_translation_context()
    {
        var script = new CollectScript();
        script.Events.Add(new OrderPlacedDomainEvent { OrderId = Guid.NewGuid() });
        var decisions = new DomainEventDecisions(
            new Dictionary<Type, DomainEventTranslatorRegistration>
            {
                [typeof(OrderPlacedDomainEvent)] = DomainEventTranslatorRegistration.ForTranslator<OrderPlacedDomainEvent, OrderTranslator>()
            },
            [],
            [],
            UndecidedDomainEventPolicy.StageAsIs);
        var probe = new TranslationProbe();
        var provider = BuildEventProvider(decisions, script, services =>
        {
            services.AddScoped(_ => new ScopedSequence { Value = 7 });
            services.AddScoped<OrderTranslator>();
            services.AddSingleton(probe);
        });

        await provider.GetRequiredService<IEventMediator>()
            .PublishAsync(new TranslateTrigger()).ConfigureAwait(false);

        probe.Sequence.Should().Be(7);
        probe.MediatedMessageType.Should().Be<TranslateTrigger>();

        var manager = provider.GetRequiredService<IOutboxManager>();
        var page = await manager.QueryAsync(
            new OutboxMessageFilter { ContractName = "tests.events.order-submitted" },
            new OutboxMessagePageRequest { PageSize = 10 }).ConfigureAwait(false);

        page.Items.Should().HaveCount(1);
    }

    /// <summary>
    ///     Verifies an ignored event is dropped before contract lookup, so it needs no contract registration.
    /// </summary>
    [Fact]
    public async Task Ignore_should_drop_the_event_without_staging_it()
    {
        var script = new CollectScript();
        script.Events.Add(new OrderPlacedDomainEvent { OrderId = Guid.NewGuid() });
        script.Events.Add(new UndocumentedDomainEvent());
        var decisions = new DomainEventDecisions(
            new Dictionary<Type, DomainEventTranslatorRegistration>(),
            [typeof(OrderPlacedDomainEvent), typeof(UndocumentedDomainEvent)],
            [],
            UndecidedDomainEventPolicy.StageAsIs);
        var provider = BuildEventProvider(decisions, script);

        await provider.GetRequiredService<IEventMediator>()
            .PublishAsync(new TranslateTrigger()).ConfigureAwait(false);

        var manager = provider.GetRequiredService<IOutboxManager>();
        var page = await manager.QueryAsync(
            new OutboxMessageFilter(),
            new OutboxMessagePageRequest { PageSize = 10 }).ConfigureAwait(false);

        page.Items.Should().BeEmpty();
    }

    /// <summary>
    ///     Verifies an event with no decision stages as-is while decided events follow their decisions.
    /// </summary>
    [Fact]
    public async Task Undecided_event_should_stage_as_is_by_default()
    {
        var script = new CollectScript();
        script.Events.Add(new OrderPlacedDomainEvent { OrderId = Guid.NewGuid() });
        script.Events.Add(new InternalDomainEvent());
        var decisions = new DomainEventDecisions(
            new Dictionary<Type, DomainEventTranslatorRegistration>(),
            [typeof(InternalDomainEvent)],
            [],
            UndecidedDomainEventPolicy.StageAsIs);
        var provider = BuildEventProvider(decisions, script);

        await provider.GetRequiredService<IEventMediator>()
            .PublishAsync(new TranslateTrigger()).ConfigureAwait(false);

        var manager = provider.GetRequiredService<IOutboxManager>();
        var placed = await manager.QueryAsync(
            new OutboxMessageFilter { ContractName = "tests.events.order-placed" },
            new OutboxMessagePageRequest { PageSize = 10 }).ConfigureAwait(false);
        var internalPage = await manager.QueryAsync(
            new OutboxMessageFilter { ContractName = "tests.events.internal" },
            new OutboxMessagePageRequest { PageSize = 10 }).ConfigureAwait(false);

        placed.Items.Should().HaveCount(1);
        internalPage.Items.Should().BeEmpty();
    }

    /// <summary>
    ///     Verifies translated messages stage directly without a further decision, even when their own type is ignored.
    /// </summary>
    [Fact]
    public async Task Translated_outputs_should_stage_without_a_further_decision()
    {
        var script = new CollectScript();
        script.Events.Add(new OrderPlacedDomainEvent { OrderId = Guid.NewGuid() });
        var decisions = new DomainEventDecisions(
            new Dictionary<Type, DomainEventTranslatorRegistration>
            {
                [typeof(OrderPlacedDomainEvent)] = DomainEventTranslatorRegistration.ForFunction<OrderPlacedDomainEvent>(
                    static _ => [new InternalDomainEvent()])
            },
            [typeof(InternalDomainEvent)],
            [],
            UndecidedDomainEventPolicy.StageAsIs);
        var provider = BuildEventProvider(decisions, script);

        await provider.GetRequiredService<IEventMediator>()
            .PublishAsync(new TranslateTrigger()).ConfigureAwait(false);

        var manager = provider.GetRequiredService<IOutboxManager>();
        var page = await manager.QueryAsync(
            new OutboxMessageFilter { ContractName = "tests.events.internal" },
            new OutboxMessagePageRequest { PageSize = 10 }).ConfigureAwait(false);

        page.Items.Should().HaveCount(1);
    }

    /// <summary>
    ///     Verifies decisions match the exact runtime type, so a derived event with a base decision stays undecided.
    /// </summary>
    [Fact]
    public async Task Decisions_should_match_the_exact_runtime_type_only()
    {
        var script = new CollectScript();
        script.Events.Add(new DerivedDomainEvent());
        var decisions = new DomainEventDecisions(
            new Dictionary<Type, DomainEventTranslatorRegistration>
            {
                [typeof(BaseDomainEvent)] = DomainEventTranslatorRegistration.ForFunction<BaseDomainEvent>(
                    static _ => [new OrderSubmittedIntegrationEvent { OrderId = Guid.NewGuid() }])
            },
            [],
            [],
            UndecidedDomainEventPolicy.StageAsIs);
        var provider = BuildEventProvider(decisions, script);

        await provider.GetRequiredService<IEventMediator>()
            .PublishAsync(new TranslateTrigger()).ConfigureAwait(false);

        var manager = provider.GetRequiredService<IOutboxManager>();
        var derived = await manager.QueryAsync(
            new OutboxMessageFilter { ContractName = "tests.events.derived" },
            new OutboxMessagePageRequest { PageSize = 10 }).ConfigureAwait(false);
        var submitted = await manager.QueryAsync(
            new OutboxMessageFilter { ContractName = "tests.events.order-submitted" },
            new OutboxMessagePageRequest { PageSize = 10 }).ConfigureAwait(false);

        derived.Items.Should().HaveCount(1);
        submitted.Items.Should().BeEmpty();
    }

    /// <summary>
    ///     Verifies an undecided event throws before the commit when a decision is required, naming the event type.
    /// </summary>
    [Fact]
    public async Task RequireDecision_should_throw_for_an_undecided_event_before_the_commit()
    {
        var script = new CollectScript();
        script.Events.Add(new OrderPlacedDomainEvent { OrderId = Guid.NewGuid() });
        var decisions = new DomainEventDecisions(
            new Dictionary<Type, DomainEventTranslatorRegistration>(),
            [],
            [],
            UndecidedDomainEventPolicy.Throw);
        var recorder = new CommitRecorder();
        var provider = BuildCommandProvider(decisions, script, recorder);

        var act = () => provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new StageDomainEventsCommand());

        var outcome = await act.Should().ThrowAsync<MessageFaultException>().ConfigureAwait(false);
        outcome.WithMessage("*OrderPlacedDomainEvent*RequireDomainEventDecision*");
        outcome.And.Code.Should().Be(DomainEventFaultCodes.UndecidedDomainEvent);
        recorder.Events.Should().BeEmpty();

        var manager = provider.GetRequiredService<IOutboxManager>();
        var page = await manager.QueryAsync(
            new OutboxMessageFilter(),
            new OutboxMessagePageRequest { PageSize = 10 }).ConfigureAwait(false);

        page.Items.Should().BeEmpty();
    }

    /// <summary>
    ///     Verifies a translator returning a null list fails the mediation before anything stages.
    /// </summary>
    [Fact]
    public async Task Translator_returning_null_should_throw_a_fault()
    {
        var script = new CollectScript();
        script.Events.Add(new OrderPlacedDomainEvent { OrderId = Guid.NewGuid() });
        var decisions = new DomainEventDecisions(
            new Dictionary<Type, DomainEventTranslatorRegistration>
            {
                [typeof(OrderPlacedDomainEvent)] = DomainEventTranslatorRegistration.ForFunction<OrderPlacedDomainEvent>(
                    static _ => (IReadOnlyList<object>) null!)
            },
            [],
            [],
            UndecidedDomainEventPolicy.StageAsIs);
        var provider = BuildCommandProvider(decisions, script);

        var act = () => provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new StageDomainEventsCommand());

        var outcome = await act.Should().ThrowAsync<MessageFaultException>().ConfigureAwait(false);
        outcome.WithMessage("*OrderPlacedDomainEvent*null list*");
        outcome.And.Code.Should().Be(DomainEventFaultCodes.InvalidTranslation);

        var manager = provider.GetRequiredService<IOutboxManager>();
        var page = await manager.QueryAsync(
            new OutboxMessageFilter(),
            new OutboxMessagePageRequest { PageSize = 10 }).ConfigureAwait(false);

        page.Items.Should().BeEmpty();
    }

    /// <summary>
    ///     Verifies a translator returning a null message fails the mediation before anything stages.
    /// </summary>
    [Fact]
    public async Task Translator_returning_a_null_message_should_throw_a_fault()
    {
        var script = new CollectScript();
        script.Events.Add(new OrderPlacedDomainEvent { OrderId = Guid.NewGuid() });
        var decisions = new DomainEventDecisions(
            new Dictionary<Type, DomainEventTranslatorRegistration>
            {
                [typeof(OrderPlacedDomainEvent)] = DomainEventTranslatorRegistration.ForFunction<OrderPlacedDomainEvent>(
                    static _ => [null!])
            },
            [],
            [],
            UndecidedDomainEventPolicy.StageAsIs);
        var provider = BuildCommandProvider(decisions, script);

        var act = () => provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new StageDomainEventsCommand());

        var outcome = await act.Should().ThrowAsync<MessageFaultException>().ConfigureAwait(false);
        outcome.WithMessage("*OrderPlacedDomainEvent*null message*");
        outcome.And.Code.Should().Be(DomainEventFaultCodes.InvalidTranslation);
    }

    /// <summary>
    ///     Verifies a translator type missing from dependency injection fails the mediation with registration guidance.
    /// </summary>
    [Fact]
    public async Task Missing_translator_type_should_throw_a_fault_with_guidance()
    {
        var script = new CollectScript();
        script.Events.Add(new OrderPlacedDomainEvent { OrderId = Guid.NewGuid() });
        var decisions = new DomainEventDecisions(
            new Dictionary<Type, DomainEventTranslatorRegistration>
            {
                [typeof(OrderPlacedDomainEvent)] = DomainEventTranslatorRegistration.ForTranslator<OrderPlacedDomainEvent, OrderTranslator>()
            },
            [],
            [],
            UndecidedDomainEventPolicy.StageAsIs);
        var provider = BuildCommandProvider(decisions, script);

        var act = () => provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new StageDomainEventsCommand());

        var outcome = await act.Should().ThrowAsync<MessageFaultException>().ConfigureAwait(false);
        outcome.WithMessage("*OrderTranslator*OrderPlacedDomainEvent*dependency injection*");
        outcome.And.Code.Should().Be(DomainEventFaultCodes.TranslatorMissing);
    }

    /// <summary>
    ///     Builds a provider that collects scripted domain events during an event mediation.
    /// </summary>
    /// <param name="decisions">The domain event decisions the staging handler reads.</param>
    /// <param name="script">The scripted domain events the handler collects.</param>
    /// <param name="configure">An optional callback registering test doubles.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildEventProvider(
        DomainEventDecisions decisions,
        CollectScript script,
        Action<ServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(script);
        services.AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddEvents(builder => builder.Register<ScriptedEventCollector>());
            registry.AddOutbox(builder =>
            {
                builder.Contracts.Register<OrderPlacedDomainEvent>("tests.events.order-placed");
                builder.Contracts.Register<OrderSubmittedIntegrationEvent>("tests.events.order-submitted");
                builder.Contracts.Register<InternalDomainEvent>("tests.events.internal");
                builder.Contracts.Register<BaseDomainEvent>("tests.events.base");
                builder.Contracts.Register<DerivedDomainEvent>("tests.events.derived");
                builder.UseInMemoryStorage();
                builder.StageCollectedEvents();
            });
        });
        services.AddTestTransactionalOutbox();
        services.AddSingleton(decisions);
        configure?.Invoke(services);

        return services.BuildServiceProvider();
    }

    /// <summary>
    ///     Builds a provider that collects scripted domain events during a command mediation, with a recording unit of work when a recorder is supplied.
    /// </summary>
    /// <param name="decisions">The domain event decisions the staging handler reads.</param>
    /// <param name="script">The scripted domain events the handler collects.</param>
    /// <param name="recorder">The commit recorder, or <see langword="null" /> for a command without a unit of work.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildCommandProvider(
        DomainEventDecisions decisions,
        CollectScript script,
        CommitRecorder? recorder = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(script);

        if (recorder is not null)
        {
            services.AddSingleton(recorder);
        }

        services.AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddCommands(builder =>
            {
                builder.Register<StageDomainEventsCommand>();
                builder.Register<ScriptedCommandCollector>();

                if (recorder is not null)
                {
                    builder.UseUnitOfWork<RecordingCommitUnitOfWork>();
                }
            });
            registry.AddOutbox(builder =>
            {
                builder.Contracts.Register<OrderPlacedDomainEvent>("tests.events.order-placed");
                builder.Contracts.Register<OrderSubmittedIntegrationEvent>("tests.events.order-submitted");
                builder.UseInMemoryStorage();
                builder.StageCollectedEvents();
            });
        });
        services.AddTestTransactionalOutbox();
        services.AddSingleton(decisions);

        return services.BuildServiceProvider();
    }

    /// <summary>
    ///     A trigger collected from during event handling tests.
    /// </summary>
    private sealed record TranslateTrigger;

    /// <summary>
    ///     A domain event collected during translation tests.
    /// </summary>
    private sealed record OrderPlacedDomainEvent
    {
        /// <summary>
        ///     Gets the order identifier.
        /// </summary>
        public Guid OrderId { get; init; }
    }

    /// <summary>
    ///     A published message produced by translation.
    /// </summary>
    private sealed record OrderSubmittedIntegrationEvent
    {
        /// <summary>
        ///     Gets the order identifier.
        /// </summary>
        public Guid OrderId { get; init; }

        /// <summary>
        ///     Gets the sequence read from a scoped service during translation.
        /// </summary>
        public int Sequence { get; init; }
    }

    /// <summary>
    ///     A domain event consumed inside the mediation and never published.
    /// </summary>
    private sealed record InternalDomainEvent;

    /// <summary>
    ///     A domain event with no contract registration, proving ignored events skip contract lookup.
    /// </summary>
    private sealed record UndocumentedDomainEvent;

    /// <summary>
    ///     A base domain event carrying a decision its derived event must not inherit.
    /// </summary>
    private record BaseDomainEvent;

    /// <summary>
    ///     A derived domain event that stays undecided when only its base has a decision.
    /// </summary>
    private sealed record DerivedDomainEvent : BaseDomainEvent;

    /// <summary>
    ///     A command collected from during staging fault tests.
    /// </summary>
    private sealed record StageDomainEventsCommand : ICommand;

    /// <summary>
    ///     Collects the scripted domain events during event handling.
    /// </summary>
    private sealed class ScriptedEventCollector : IEventHandler<TranslateTrigger>
    {
        /// <summary>
        ///     The mediation-scoped domain event collector.
        /// </summary>
        private readonly IDomainEventCollector _collector;

        /// <summary>
        ///     The scripted domain events to collect.
        /// </summary>
        private readonly CollectScript _script;

        /// <summary>
        ///     Initializes a new instance of the <see cref="ScriptedEventCollector" /> class.
        /// </summary>
        /// <param name="collector">The mediation-scoped domain event collector.</param>
        /// <param name="script">The scripted domain events to collect.</param>
        public ScriptedEventCollector(IDomainEventCollector collector, CollectScript script)
        {
            _collector = collector;
            _script = script;
        }

        /// <inheritdoc />
        public Task HandleAsync(TranslateTrigger message, CancellationToken cancellationToken = default)
        {
            _script.ApplyTo(_collector);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    ///     Collects the scripted domain events during command handling.
    /// </summary>
    private sealed class ScriptedCommandCollector : ICommandHandler<StageDomainEventsCommand>
    {
        /// <summary>
        ///     The mediation-scoped domain event collector.
        /// </summary>
        private readonly IDomainEventCollector _collector;

        /// <summary>
        ///     The scripted domain events to collect.
        /// </summary>
        private readonly CollectScript _script;

        /// <summary>
        ///     Initializes a new instance of the <see cref="ScriptedCommandCollector" /> class.
        /// </summary>
        /// <param name="collector">The mediation-scoped domain event collector.</param>
        /// <param name="script">The scripted domain events to collect.</param>
        public ScriptedCommandCollector(IDomainEventCollector collector, CollectScript script)
        {
            _collector = collector;
            _script = script;
        }

        /// <inheritdoc />
        public Task HandleAsync(StageDomainEventsCommand message, CancellationToken cancellationToken = default)
        {
            _script.ApplyTo(_collector);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    ///     Holds the domain events one test collects.
    /// </summary>
    private sealed class CollectScript
    {
        /// <summary>
        ///     Gets the domain events to collect.
        /// </summary>
        public List<object> Events { get; } = [];

        /// <summary>
        ///     Collects every scripted domain event.
        /// </summary>
        /// <param name="collector">The mediation-scoped domain event collector.</param>
        public void ApplyTo(IDomainEventCollector collector)
        {
            foreach (var domainEvent in Events)
            {
                collector.Collect(domainEvent);
            }
        }
    }

    /// <summary>
    ///     Records what a translator observed during translation.
    /// </summary>
    private sealed class TranslationProbe
    {
        /// <summary>
        ///     Gets or sets the sequence read from the scoped service.
        /// </summary>
        public int Sequence { get; set; } = -1;

        /// <summary>
        ///     Gets or sets the mediated message type carried by the translation context.
        /// </summary>
        public Type? MediatedMessageType { get; set; }
    }

    /// <summary>
    ///     A scoped service read during translation.
    /// </summary>
    private sealed class ScopedSequence
    {
        /// <summary>
        ///     Gets or sets the sequence value.
        /// </summary>
        public int Value { get; set; }
    }

    /// <summary>
    ///     Translates through a scoped service and records the translation context.
    /// </summary>
    private sealed class OrderTranslator : IDomainEventTranslator<OrderPlacedDomainEvent>
    {
        /// <summary>
        ///     The recorded translation observations shared with the test.
        /// </summary>
        private readonly TranslationProbe _probe;

        /// <summary>
        ///     The scoped sequence read during translation.
        /// </summary>
        private readonly ScopedSequence _sequence;

        /// <summary>
        ///     Initializes a new instance of the <see cref="OrderTranslator" /> class.
        /// </summary>
        /// <param name="sequence">The scoped sequence read during translation.</param>
        /// <param name="probe">The recorded translation observations shared with the test.</param>
        public OrderTranslator(ScopedSequence sequence, TranslationProbe probe)
        {
            _sequence = sequence;
            _probe = probe;
        }

        /// <inheritdoc />
        public ValueTask<IReadOnlyList<object>> TranslateAsync(
            OrderPlacedDomainEvent domainEvent,
            DomainEventTranslationContext context,
            CancellationToken cancellationToken)
        {
            _probe.Sequence = _sequence.Value;
            _probe.MediatedMessageType = context.MediatedMessageType;

            IReadOnlyList<object> translated =
            [
                new OrderSubmittedIntegrationEvent { OrderId = domainEvent.OrderId, Sequence = _sequence.Value }
            ];

            return new ValueTask<IReadOnlyList<object>>(translated);
        }
    }

    /// <summary>
    ///     Collects the commits observed during one test run.
    /// </summary>
    private sealed class CommitRecorder
    {
        /// <summary>
        ///     Gets the observed commits, in the order they happened.
        /// </summary>
        public List<string> Events { get; } = [];
    }

    /// <summary>
    ///     A unit of work that records each commit instead of touching storage.
    /// </summary>
    private sealed class RecordingCommitUnitOfWork : IUnitOfWork
    {
        /// <summary>
        ///     The recorder shared with the test.
        /// </summary>
        private readonly CommitRecorder _recorder;

        /// <summary>
        ///     Initializes a new instance of the <see cref="RecordingCommitUnitOfWork" /> class.
        /// </summary>
        /// <param name="recorder">The recorder shared with the test.</param>
        public RecordingCommitUnitOfWork(CommitRecorder recorder)
        {
            _recorder = recorder;
        }

        /// <inheritdoc />
        public Task CommitAsync(CancellationToken cancellationToken = default)
        {
            _recorder.Events.Add("commit");
            return Task.CompletedTask;
        }
    }
}
