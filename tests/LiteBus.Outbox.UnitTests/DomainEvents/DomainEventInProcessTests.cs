using System.Collections.Concurrent;
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
using LiteBus.Queries;
using LiteBus.Queries.Abstractions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Outbox.UnitTests.DomainEvents;

/// <summary>
///     Verifies domain events publish in process as root mediations with a cause, after the commit when one notifies the commit listener and when staging completes otherwise.
/// </summary>
[Collection("Sequential")]
public sealed class DomainEventInProcessTests : LiteBusTestBase
{
    /// <summary>
    ///     Verifies an event mediation publishes in process when staging completes, as a root mediation continuing the ambient correlation.
    /// </summary>
    [Fact]
    public async Task Event_mediation_should_publish_in_process_as_a_root_with_a_cause()
    {
        var script = new CollectScript();
        var noteId = Guid.NewGuid();
        script.Events.Add(new AuditNoteRecorded { NoteId = noteId });
        var probe = new InProcessReactionProbe();
        var provider = BuildEventProvider(PublishDecisions(), script, probe);
        var settings = new EventMediationSettings
        {
            Items = new Dictionary<string, object>
            {
                [MessageTraceContextKeys.CorrelationId] = "correlation-9",
                [MessageTraceContextKeys.CausationId] = "causation-3"
            }
        };

        await provider.GetRequiredService<IEventMediator>()
            .PublishAsync(new InProcessTrigger(), settings).ConfigureAwait(false);

        probe.Events.Should().Equal("audit-note");
        probe.HadParent.Should().BeFalse();
        probe.Cause.Should().NotBeNull();
        probe.Cause!.Source.Should().Be(MediationCauseSource.DomainEvent);
        probe.Cause.CorrelationId.Should().Be("correlation-9");
        probe.Cause.CausationId.Should().NotBeNullOrWhiteSpace();
        probe.Cause.CausationId.Should().NotBe("causation-3");

        var manager = provider.GetRequiredService<IOutboxManager>();
        var page = await manager.QueryAsync(
            new OutboxMessageFilter(),
            new OutboxMessagePageRequest { PageSize = 10 }).ConfigureAwait(false);

        page.Items.Should().BeEmpty();
    }

    /// <summary>
    ///     Verifies in-process publication restores the causing tenant without its principal and propagates trace metadata.
    /// </summary>
    [Fact]
    public async Task In_process_publication_should_propagate_tenant_and_trace()
    {
        var script = new CollectScript();
        script.Events.Add(new AuditNoteRecorded { NoteId = Guid.NewGuid() });
        var probe = new InProcessReactionProbe();
        var provider = BuildEventProvider(PublishDecisions(), script, probe);
        var settings = EventMediationSettings.ForTenant("tenant-7");
        settings.Items[MessageTraceContextKeys.CorrelationId] = "correlation-9";

        await provider.GetRequiredService<IEventMediator>()
            .PublishAsync(new InProcessTrigger(), settings).ConfigureAwait(false);

        probe.Events.Should().Equal("audit-note");
        probe.TenantId.Should().Be("tenant-7");
        probe.CorrelationId.Should().Be("correlation-9");
        probe.CausationId.Should().Be(probe.Cause!.CausationId);
    }

    /// <summary>
    ///     Verifies plain domain events and interface events both publish in process.
    /// </summary>
    [Fact]
    public async Task In_process_publication_should_publish_plain_and_interface_events()
    {
        var script = new CollectScript();
        script.Events.Add(new AuditNoteRecorded { NoteId = Guid.NewGuid() });
        script.Events.Add(new ErasureCompleted { AggregateId = Guid.NewGuid() });
        var probe = new InProcessReactionProbe();
        var decisions = new DomainEventDecisions(
            new Dictionary<Type, DomainEventTranslatorRegistration>(),
            [],
            [typeof(AuditNoteRecorded), typeof(ErasureCompleted)],
            UndecidedDomainEventPolicy.StageAsIs);
        var provider = BuildEventProvider(decisions, script, probe);

        await provider.GetRequiredService<IEventMediator>()
            .PublishAsync(new InProcessTrigger()).ConfigureAwait(false);

        probe.Events.Should().Equal("audit-note", "erasure");
    }

    /// <summary>
    ///     Verifies a command with a unit of work publishes in process after the commit, as a root mediation with a cause.
    /// </summary>
    [Fact]
    public async Task Command_with_unit_of_work_should_publish_in_process_after_the_commit()
    {
        var script = new CollectScript();
        script.Events.Add(new AuditNoteRecorded { NoteId = Guid.NewGuid() });
        var probe = new InProcessReactionProbe();
        var recorder = new CommitRecorder();
        var provider = BuildCommandProvider(PublishDecisions(), script, probe, recorder, useUnitOfWork: true);

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new PublishDomainEventsCommand()).ConfigureAwait(false);

        recorder.Events.Should().Equal("commit", "reaction");
        probe.HadParent.Should().BeFalse();
        probe.Cause.Should().NotBeNull();
        probe.Cause!.Source.Should().Be(MediationCauseSource.DomainEvent);

        var manager = provider.GetRequiredService<IOutboxManager>();
        var page = await manager.QueryAsync(
            new OutboxMessageFilter(),
            new OutboxMessagePageRequest { PageSize = 10 }).ConfigureAwait(false);

        page.Items.Should().BeEmpty();
    }

    /// <summary>
    ///     Verifies a command without a unit of work publishes in process when staging completes, having no commit to wait for.
    /// </summary>
    [Fact]
    public async Task Command_without_unit_of_work_should_publish_in_process_when_staging_completes()
    {
        var script = new CollectScript();
        script.Events.Add(new AuditNoteRecorded { NoteId = Guid.NewGuid() });
        var probe = new InProcessReactionProbe();
        var recorder = new CommitRecorder();
        var provider = BuildCommandProvider(PublishDecisions(), script, probe, recorder, useUnitOfWork: false);

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new PublishDomainEventsCommand()).ConfigureAwait(false);

        recorder.Events.Should().Equal("reaction");
        probe.Cause.Should().NotBeNull();
        probe.Cause!.Source.Should().Be(MediationCauseSource.DomainEvent);
    }

    /// <summary>
    ///     Verifies a query with a unit of work publishes in process after the commit.
    /// </summary>
    [Fact]
    public async Task Query_with_unit_of_work_should_publish_in_process_after_the_commit()
    {
        var script = new CollectScript();
        script.Events.Add(new AuditNoteRecorded { NoteId = Guid.NewGuid() });
        var probe = new InProcessReactionProbe();
        var recorder = new CommitRecorder();
        var provider = BuildQueryProvider(PublishDecisions(), script, probe, recorder);

        var result = await provider.GetRequiredService<IQueryMediator>()
            .QueryAsync(new PublishDomainEventsQuery()).ConfigureAwait(false);

        result.Should().Be("produced");
        recorder.Events.Should().Equal("commit", "reaction");
        probe.Cause.Should().NotBeNull();
        probe.Cause!.Source.Should().Be(MediationCauseSource.DomainEvent);
    }

    /// <summary>
    ///     Verifies in-process publication without a registered event mediator fails before the commit with guidance.
    /// </summary>
    [Fact]
    public async Task Missing_event_mediator_should_throw_before_the_commit()
    {
        var script = new CollectScript();
        script.Events.Add(new AuditNoteRecorded { NoteId = Guid.NewGuid() });
        var recorder = new CommitRecorder();
        var services = new ServiceCollection();
        services.AddSingleton(script);
        services.AddSingleton(recorder);
        services.AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddCommands(builder =>
            {
                builder.Register<PublishDomainEventsCommand>();
                builder.Register<ScriptedCommandCollector>();
                builder.UseUnitOfWork<RecordingCommitUnitOfWork>();
            });
            registry.AddOutbox(builder =>
            {
                builder.UseInMemoryStorage();
                builder.StageCollectedEvents();
            });
        });
        services.AddTestTransactionalOutbox();
        services.AddSingleton(PublishDecisions());
        var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new PublishDomainEventsCommand());

        var outcome = await act.Should().ThrowAsync<MessageFaultException>().ConfigureAwait(false);
        outcome.WithMessage("*IEventMediator*AddEvents*");
        outcome.And.Code.Should().Be(DomainEventFaultCodes.EventMediatorMissing);
        recorder.Events.Should().BeEmpty();
    }

    /// <summary>
    ///     Verifies buffering without a registered commit listener fails before the commit, so recorded events are never stranded silently.
    /// </summary>
    [Fact]
    public async Task Missing_publish_listener_should_throw_before_the_commit()
    {
        var script = new CollectScript();
        script.Events.Add(new AuditNoteRecorded { NoteId = Guid.NewGuid() });
        var probe = new InProcessReactionProbe();
        var recorder = new CommitRecorder();
        var services = new ServiceCollection();
        services.AddSingleton(script);
        services.AddSingleton(probe);
        services.AddSingleton(recorder);
        services.AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddCommands(builder =>
            {
                builder.Register<PublishDomainEventsCommand>();
                builder.Register<ScriptedCommandCollector>();
                builder.UseUnitOfWork<RecordingCommitUnitOfWork>();
            });
            registry.AddEvents(builder => builder.Register<AuditNoteReaction>());
            registry.AddOutbox(builder =>
            {
                builder.UseInMemoryStorage();
                builder.StageCollectedEvents();
            });
        });
        services.AddTestTransactionalOutbox();
        services.AddSingleton(PublishDecisions());
        services.AddScoped<StagedInProcessDomainEvents>();

        foreach (var stale in services.Where(static descriptor => descriptor.ServiceType == typeof(IUnitOfWorkCommitListener)).ToList())
        {
            services.Remove(stale);
        }

        var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new PublishDomainEventsCommand());

        var outcome = await act.Should().ThrowAsync<MessageFaultException>().ConfigureAwait(false);
        outcome.WithMessage("*commit listener*");
        outcome.And.Code.Should().Be(DomainEventFaultCodes.InProcessPublishUnavailable);
        recorder.Events.Should().BeEmpty();
        probe.Events.Should().BeEmpty();
    }

    /// <summary>
    ///     Verifies the commit listener drains the recorded buffer and publishes each event with its recorded cause.
    /// </summary>
    [Fact]
    public async Task Listener_should_drain_the_buffer_and_publish_with_the_recorded_cause()
    {
        var probe = new InProcessReactionProbe();
        var services = new ServiceCollection();
        services.AddSingleton(probe);
        services.AddSingleton(new CommitRecorder());
        services.AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddEvents(builder => builder.Register<AuditNoteReaction>());
        });
        var provider = services.BuildServiceProvider();
        var buffer = new StagedInProcessDomainEvents();
        var cause = new MediationCause("correlation-9", "causation-3", MediationCauseSource.DomainEvent);
        buffer.Add(new StagedInProcessDomainEvent(
            new AuditNoteRecorded { NoteId = Guid.NewGuid() },
            cause,
            "tenant-7",
            null));
        var listener = new DomainEventInProcessPublishListener(buffer, provider);

        await listener.OnCommittedAsync().ConfigureAwait(false);

        probe.Events.Should().Equal("audit-note");
        probe.Cause.Should().Be(cause);
        probe.TenantId.Should().Be("tenant-7");
        probe.HadParent.Should().BeFalse();
        buffer.IsEmpty.Should().BeTrue();
    }

    /// <summary>
    ///     Verifies the commit listener with an empty buffer returns without resolving the event mediator.
    /// </summary>
    [Fact]
    public async Task Listener_with_empty_buffer_should_resolve_nothing()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var listener = new DomainEventInProcessPublishListener(new StagedInProcessDomainEvents(), provider);

        await listener.OnCommittedAsync().ConfigureAwait(false);
    }

    /// <summary>
    ///     Verifies the commit listener with recorded events and no event mediator throws guidance.
    /// </summary>
    [Fact]
    public async Task Listener_without_event_mediator_should_throw_guidance()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var buffer = new StagedInProcessDomainEvents();
        buffer.Add(new StagedInProcessDomainEvent(
            new AuditNoteRecorded { NoteId = Guid.NewGuid() },
            new MediationCause("correlation-9", "causation-3", MediationCauseSource.DomainEvent),
            null,
            null));
        var listener = new DomainEventInProcessPublishListener(buffer, provider);

        var act = () => listener.OnCommittedAsync();

        (await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false))
            .WithMessage("*IEventMediator*AddEvents*");
    }

    /// <summary>
    ///     Builds the decisions publishing the audit note domain event in process.
    /// </summary>
    /// <returns>The domain event decisions.</returns>
    private static DomainEventDecisions PublishDecisions()
    {
        return new DomainEventDecisions(
            new Dictionary<Type, DomainEventTranslatorRegistration>(),
            [],
            [typeof(AuditNoteRecorded)],
            UndecidedDomainEventPolicy.StageAsIs);
    }

    /// <summary>
    ///     Builds a provider that collects scripted domain events during an event mediation.
    /// </summary>
    /// <param name="decisions">The domain event decisions the staging handler reads.</param>
    /// <param name="script">The scripted domain events the handler collects.</param>
    /// <param name="probe">The recorded reaction observations shared with the test.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildEventProvider(
        DomainEventDecisions decisions,
        CollectScript script,
        InProcessReactionProbe probe)
    {
        var services = new ServiceCollection();
        services.AddSingleton(script);
        services.AddSingleton(probe);
        services.AddSingleton(new CommitRecorder());
        services.AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddEvents(builder =>
            {
                builder.Register<ScriptedEventCollector>();
                builder.Register<AuditNoteReaction>();
                builder.Register<ErasureReaction>();
            });
            registry.AddOutbox(builder =>
            {
                builder.UseInMemoryStorage();
                builder.StageCollectedEvents();
            });
        });
        services.AddTestTransactionalOutbox();
        services.AddSingleton(decisions);
        services.AddScoped<StagedInProcessDomainEvents>();
        services.AddScoped<IUnitOfWorkCommitListener, DomainEventInProcessPublishListener>();

        return services.BuildServiceProvider();
    }

    /// <summary>
    ///     Builds a provider that collects scripted domain events during a command mediation.
    /// </summary>
    /// <param name="decisions">The domain event decisions the staging handler reads.</param>
    /// <param name="script">The scripted domain events the handler collects.</param>
    /// <param name="probe">The recorded reaction observations shared with the test.</param>
    /// <param name="recorder">The recorded commits and reactions shared with the test.</param>
    /// <param name="useUnitOfWork"><see langword="true" /> to commit through a recording unit of work.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildCommandProvider(
        DomainEventDecisions decisions,
        CollectScript script,
        InProcessReactionProbe probe,
        CommitRecorder recorder,
        bool useUnitOfWork)
    {
        var services = new ServiceCollection();
        services.AddSingleton(script);
        services.AddSingleton(probe);
        services.AddSingleton(recorder);
        services.AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddCommands(builder =>
            {
                builder.Register<PublishDomainEventsCommand>();
                builder.Register<ScriptedCommandCollector>();

                if (useUnitOfWork)
                {
                    builder.UseUnitOfWork<RecordingCommitUnitOfWork>();
                }
            });
            registry.AddEvents(builder => builder.Register<AuditNoteReaction>());
            registry.AddOutbox(builder =>
            {
                builder.UseInMemoryStorage();
                builder.StageCollectedEvents();
            });
        });
        services.AddTestTransactionalOutbox();
        services.AddSingleton(decisions);
        services.AddScoped<StagedInProcessDomainEvents>();
        services.AddScoped<IUnitOfWorkCommitListener, DomainEventInProcessPublishListener>();

        return services.BuildServiceProvider();
    }

    /// <summary>
    ///     Builds a provider that collects scripted domain events during a query mediation.
    /// </summary>
    /// <param name="decisions">The domain event decisions the staging handler reads.</param>
    /// <param name="script">The scripted domain events the handler collects.</param>
    /// <param name="probe">The recorded reaction observations shared with the test.</param>
    /// <param name="recorder">The recorded commits and reactions shared with the test.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildQueryProvider(
        DomainEventDecisions decisions,
        CollectScript script,
        InProcessReactionProbe probe,
        CommitRecorder recorder)
    {
        var services = new ServiceCollection();
        services.AddSingleton(script);
        services.AddSingleton(probe);
        services.AddSingleton(recorder);
        services.AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddQueries(builder =>
            {
                builder.Register<PublishDomainEventsQuery>();
                builder.Register<ScriptedQueryCollector>();
                builder.UseUnitOfWork<RecordingCommitUnitOfWork>();
            });
            registry.AddEvents(builder => builder.Register<AuditNoteReaction>());
            registry.AddOutbox(builder =>
            {
                builder.UseInMemoryStorage();
                builder.StageCollectedEvents();
            });
        });
        services.AddTestTransactionalOutbox();
        services.AddSingleton(decisions);
        services.AddScoped<StagedInProcessDomainEvents>();
        services.AddScoped<IUnitOfWorkCommitListener, DomainEventInProcessPublishListener>();

        return services.BuildServiceProvider();
    }

    /// <summary>
    ///     A trigger collected from during in-process event tests.
    /// </summary>
    private sealed record InProcessTrigger;

    /// <summary>
    ///     A plain domain event published in process.
    /// </summary>
    private sealed record AuditNoteRecorded
    {
        /// <summary>
        ///     Gets the note identifier.
        /// </summary>
        public Guid NoteId { get; init; }
    }

    /// <summary>
    ///     An interface domain event published in process.
    /// </summary>
    private sealed record ErasureCompleted : IEvent
    {
        /// <summary>
        ///     Gets the aggregate identifier.
        /// </summary>
        public Guid AggregateId { get; init; }
    }

    /// <summary>
    ///     A command collected from during in-process command tests.
    /// </summary>
    private sealed record PublishDomainEventsCommand : ICommand;

    /// <summary>
    ///     A query collected from during in-process query tests.
    /// </summary>
    private sealed record PublishDomainEventsQuery : IQuery<string>;

    /// <summary>
    ///     Collects the scripted domain events during event handling.
    /// </summary>
    private sealed class ScriptedEventCollector : IEventHandler<InProcessTrigger>
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
        public Task HandleAsync(InProcessTrigger message, CancellationToken cancellationToken = default)
        {
            _script.ApplyTo(_collector);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    ///     Collects the scripted domain events during command handling.
    /// </summary>
    private sealed class ScriptedCommandCollector : ICommandHandler<PublishDomainEventsCommand>
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
        public Task HandleAsync(PublishDomainEventsCommand message, CancellationToken cancellationToken = default)
        {
            _script.ApplyTo(_collector);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    ///     Collects the scripted domain events during query handling.
    /// </summary>
    private sealed class ScriptedQueryCollector : IQueryHandler<PublishDomainEventsQuery, string>
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
        ///     Initializes a new instance of the <see cref="ScriptedQueryCollector" /> class.
        /// </summary>
        /// <param name="collector">The mediation-scoped domain event collector.</param>
        /// <param name="script">The scripted domain events to collect.</param>
        public ScriptedQueryCollector(IDomainEventCollector collector, CollectScript script)
        {
            _collector = collector;
            _script = script;
        }

        /// <inheritdoc />
        public Task<string> HandleAsync(PublishDomainEventsQuery message, CancellationToken cancellationToken = default)
        {
            _script.ApplyTo(_collector);
            return Task.FromResult("produced");
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
    ///     Reacts to the audit note domain event and records the reaction mediation.
    /// </summary>
    private sealed class AuditNoteReaction : IEventHandler<AuditNoteRecorded>
    {
        /// <summary>
        ///     The recorded reaction observations shared with the test.
        /// </summary>
        private readonly InProcessReactionProbe _probe;

        /// <summary>
        ///     The recorded commits and reactions shared with the test.
        /// </summary>
        private readonly CommitRecorder _recorder;

        /// <summary>
        ///     Initializes a new instance of the <see cref="AuditNoteReaction" /> class.
        /// </summary>
        /// <param name="probe">The recorded reaction observations shared with the test.</param>
        /// <param name="recorder">The recorded commits and reactions shared with the test.</param>
        public AuditNoteReaction(InProcessReactionProbe probe, CommitRecorder recorder)
        {
            _probe = probe;
            _recorder = recorder;
        }

        /// <inheritdoc />
        public Task HandleAsync(AuditNoteRecorded message, CancellationToken cancellationToken = default)
        {
            _probe.CaptureFromAmbient();
            _probe.Events.Enqueue("audit-note");
            _recorder.Events.Enqueue("reaction");
            return Task.CompletedTask;
        }
    }

    /// <summary>
    ///     Reacts to the erasure domain event and records that it ran.
    /// </summary>
    private sealed class ErasureReaction : IEventHandler<ErasureCompleted>
    {
        /// <summary>
        ///     The recorded reaction observations shared with the test.
        /// </summary>
        private readonly InProcessReactionProbe _probe;

        /// <summary>
        ///     Initializes a new instance of the <see cref="ErasureReaction" /> class.
        /// </summary>
        /// <param name="probe">The recorded reaction observations shared with the test.</param>
        public ErasureReaction(InProcessReactionProbe probe)
        {
            _probe = probe;
        }

        /// <inheritdoc />
        public Task HandleAsync(ErasureCompleted message, CancellationToken cancellationToken = default)
        {
            _probe.Events.Enqueue("erasure");
            return Task.CompletedTask;
        }
    }

    /// <summary>
    ///     Records what an in-process reaction observed about its mediation.
    /// </summary>
    private sealed class InProcessReactionProbe
    {
        /// <summary>
        ///     Gets the reactions that ran, in the order they ran.
        /// </summary>
        public ConcurrentQueue<string> Events { get; } = new();

        /// <summary>
        ///     Gets or sets the cause the reaction mediation carried.
        /// </summary>
        public MediationCause? Cause { get; set; }

        /// <summary>
        ///     Gets or sets a value indicating whether the reaction mediation had a parent.
        /// </summary>
        public bool HadParent { get; set; }

        /// <summary>
        ///     Gets or sets the tenant the reaction mediation ran in.
        /// </summary>
        public string? TenantId { get; set; }

        /// <summary>
        ///     Gets or sets the correlation identifier the reaction mediation observed.
        /// </summary>
        public string? CorrelationId { get; set; }

        /// <summary>
        ///     Gets or sets the causation identifier the reaction mediation observed.
        /// </summary>
        public string? CausationId { get; set; }

        /// <summary>
        ///     Captures the ambient lineage, identity, and trace metadata of the running reaction.
        /// </summary>
        public void CaptureFromAmbient()
        {
            var ambient = AmbientExecutionContext.GetCurrentOrDefault();
            Cause = ambient?.Cause;
            HadParent = ambient?.Parent is not null;
            TenantId = ambient?.Identity.TenantId;
            CorrelationId = ReadItem(ambient, MessageTraceContextKeys.CorrelationId);
            CausationId = ReadItem(ambient, MessageTraceContextKeys.CausationId);
        }

        /// <summary>
        ///     Reads one trace metadata item from an execution context.
        /// </summary>
        /// <param name="ambient">The execution context, or <see langword="null" /> when no mediation is ambient.</param>
        /// <param name="key">The trace metadata item key.</param>
        /// <returns>The trace value, or <see langword="null" /> when the context carries none.</returns>
        private static string? ReadItem(IExecutionContext? ambient, string key)
        {
            return ambient is not null && ambient.Items.TryGetValue(key, out var value)
                ? value as string
                : null;
        }
    }

    /// <summary>
    ///     Collects the commits and reactions observed during one test run.
    /// </summary>
    private sealed class CommitRecorder
    {
        /// <summary>
        ///     Gets the observed commits and reactions, in the order they happened.
        /// </summary>
        public ConcurrentQueue<string> Events { get; } = new();
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
            _recorder.Events.Enqueue("commit");
            return Task.CompletedTask;
        }
    }
}
