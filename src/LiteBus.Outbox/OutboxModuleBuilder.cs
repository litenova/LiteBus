using System;
using System.Collections.Generic;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Processing;
using LiteBus.Outbox.DomainEvents;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Diagnostics;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Configures the outbox module and collects its sub-modules.
///     Extension methods for specific backends call
///     <see cref="RegisterStorage" /> or <see cref="RegisterDispatcher" /> on this builder.
/// </summary>
public sealed class OutboxModuleBuilder
{
    /// <summary>
    ///     Deferred contract registrations applied when the outbox module builds.
    /// </summary>
    private readonly MessageContractBuilder _contracts = new();

    /// <summary>
    ///     Consumer-owned diagnostic probes registered for this outbox.
    /// </summary>
    private readonly List<DiagnosticCheckRegistration> _diagnosticChecks = [];

    /// <summary>
    ///     The configured domain event accessor pairs in registration order.
    /// </summary>
    private readonly List<DomainEventAccessor> _domainEventAccessors = [];

    /// <summary>
    ///     The translator registrations by closed domain event type.
    /// </summary>
    private readonly Dictionary<Type, DomainEventTranslatorRegistration> _domainEventTranslators = [];

    /// <summary>
    ///     The domain event types dropped without staging.
    /// </summary>
    private readonly HashSet<Type> _ignoredDomainEvents = [];

    /// <summary>
    ///     The domain event types published in process after the commit.
    /// </summary>
    private readonly HashSet<Type> _inProcessDomainEvents = [];

    /// <summary>
    ///     The domain event types staged unchanged.
    /// </summary>
    private readonly HashSet<Type> _stagedDomainEvents = [];

    /// <summary>
    ///     What the staging handler does with a domain event type that has no registered decision.
    /// </summary>
    private UndecidedDomainEventPolicy _undecidedDomainEventPolicy = UndecidedDomainEventPolicy.StageAsIs;

    /// <summary>
    ///     The configured dispatcher sub-module, if any.
    /// </summary>
    private IOutboxDispatcherModule? _dispatcherModule;

    /// <summary>
    ///     The processor options supplied by the application or the framework defaults.
    /// </summary>
    private OutboxProcessorOptions _processorOptions = new();

    /// <summary>
    ///     The contract resolver registered through <see cref="UseContractResolver" />, if any.
    /// </summary>
    private IMessageContractResolver? _contractResolver;

    /// <summary>
    ///     Whether <see cref="EnableCleanup" /> was called.
    /// </summary>
    private bool _enableCleanup;

    /// <summary>
    ///     Whether <see cref="EnableOutboxProcessor" /> was called.
    /// </summary>
    private bool _enableOutboxProcessor;

    /// <summary>
    ///     The optional payload encryptor registered through <see cref="UsePayloadEncryption" />.
    /// </summary>
    private IPayloadEncryptor? _payloadEncryptor;

    /// <summary>
    ///     The configured storage sub-module, if any.
    /// </summary>
    private IOutboxStorageModule? _storageModule;

    /// <summary>
    ///     Whether <see cref="StageCollectedEvents" /> was called.
    /// </summary>
    private bool _stageCollectedEvents;

    /// <summary>
    ///     Whether <see cref="UseAmbientTrace" /> was called.
    /// </summary>
    private bool _useAmbientTrace;

    /// <summary>
    ///     Whether <see cref="UseAmbientTenant" /> was called.
    /// </summary>
    private bool _useAmbientTenant;

    /// <summary>
    ///     Whether <see cref="EnablePerHandlerDelivery" /> was called.
    /// </summary>
    private bool _perHandlerDelivery;

    /// <summary>
    ///     The immediate dispatch options, created by <see cref="EnableImmediateDispatch" />; <see langword="null" />
    ///     while immediate dispatch is off.
    /// </summary>
    private OutboxImmediateDispatchOptions? _immediateDispatch;

    /// <summary>
    ///     Whether <see cref="AllowPerHandlerDeliveryWithTransportDispatch" /> was called.
    /// </summary>
    private bool _allowPerHandlerDeliveryWithTransportDispatch;

    /// <summary>
    ///     The reaction retry policy registered through <see cref="UseReactionRetryPolicy{TPolicy}" /> or
    ///     <see cref="UseReactionRetryPolicyInstance" />, as an implementation type or a pre-created instance.
    /// </summary>
    private object? _reactionRetryPolicy;

    /// <summary>
    ///     The lifetime a reaction retry policy implementation type is registered with.
    /// </summary>
    private InstanceLifetime _reactionRetryPolicyLifetime = InstanceLifetime.Singleton;

    /// <summary>
    ///     Gets the deferred contract writer. Registrations are applied to the shared
    ///     <see cref="IMessageContractRegistry" /> when the module builds.
    /// </summary>
    public IContractWriter Contracts => _contracts;

    /// <summary>
    ///     Gets the options controlling processor batch size, lease duration, and retry.
    /// </summary>
    public OutboxProcessorOptions ProcessorOptions => _processorOptions.HookFailurePolicy.HasValue || _dispatcherModule is null
        ? _processorOptions
        : _processorOptions with
        {
            HookFailurePolicy = _dispatcherModule.DefaultHookFailurePolicy
        };

    /// <summary>
    ///     Gets the options controlling the background service lifecycle and poll behaviour.
    /// </summary>
    public OutboxProcessorHostOptions ProcessorHostOptions { get; } = new();

    /// <summary>
    ///     Gets the options for the optional outbox retention cleanup loop.
    /// </summary>
    public OutboxCleanupHostOptions CleanupHostOptions { get; } = new();

    /// <summary>
    ///     Gets the options controlling in-dispatch retries of reactions that met a storage conflict.
    /// </summary>
    public OutboxReactionRetryOptions ReactionRetryOptions { get; } = new();

    /// <summary>
    ///     Gets a value indicating whether the outbox processor background service is registered.
    /// </summary>
    public bool IsOutboxProcessorEnabled => _enableOutboxProcessor;

    /// <summary>
    ///     Gets a value indicating whether the outbox cleanup background service is registered.
    /// </summary>
    public bool IsCleanupEnabled => _enableCleanup;

    /// <summary>
    ///     Gets a value indicating whether a storage sub-module was registered on this builder.
    /// </summary>
    public bool IsStorageConfigured => _storageModule is not null;

    /// <summary>
    ///     Gets a value indicating whether a dispatcher sub-module was registered on this builder.
    /// </summary>
    public bool IsDispatcherConfigured => _dispatcherModule is not null;

    /// <summary>
    ///     Activates the background processor that polls for and dispatches
    ///     pending outbox messages.
    /// </summary>
    /// <param name="configure">An optional callback that configures poll interval, startup delay, and adaptive polling.</param>
    /// <returns>The current builder.</returns>
    public OutboxModuleBuilder EnableOutboxProcessor(Action<OutboxProcessorHostOptions>? configure = null)
    {
        _enableOutboxProcessor = true;
        configure?.Invoke(ProcessorHostOptions);
        return this;
    }

    /// <summary>
    ///     Registers the outbox retention cleanup background loop for the generic host.
    /// </summary>
    /// <param name="configure">An optional callback that configures cleanup interval and retention.</param>
    /// <returns>The current builder.</returns>
    public OutboxModuleBuilder EnableCleanup(Action<OutboxCleanupHostOptions>? configure = null)
    {
        _enableCleanup = true;
        configure?.Invoke(CleanupHostOptions);
        return this;
    }

    /// <summary>
    ///     Replaces the default processor options.
    /// </summary>
    /// <param name="options">The batch, lease, owner, and retry options used by the processor.</param>
    /// <returns>The current builder.</returns>
    public OutboxModuleBuilder UseProcessorOptions(OutboxProcessorOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _processorOptions = options;

        return this;
    }

    /// <summary>
    ///     Configures how the in-process dispatcher retries a reaction that met a storage conflict.
    /// </summary>
    /// <param name="configure">An optional callback that adjusts the retry bound and delay curve.</param>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     The values shape the default retry curve and the per-dispatch safety bound, which the dispatcher
    ///     enforces for every policy. A replacement registered through
    ///     <see cref="UseReactionRetryPolicy{TPolicy}" /> or <see cref="UseReactionRetryPolicyInstance" /> still
    ///     observes <see cref="OutboxReactionRetryOptions.MaxAttempts" />; raise it when the replacement needs more
    ///     attempts.
    /// </remarks>
    public OutboxModuleBuilder UseReactionRetry(Action<OutboxReactionRetryOptions>? configure = null)
    {
        configure?.Invoke(ReactionRetryOptions);
        return this;
    }

    /// <summary>
    ///     Registers the policy that decides whether a conflicted reaction publishes again.
    /// </summary>
    /// <typeparam name="TPolicy">The policy implementation, constructed from the dispatch scope.</typeparam>
    /// <param name="lifetime">The lifetime the implementation is registered with. Singleton by default.</param>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     The dispatcher resolves the policy from a fresh dispatch scope per conflict and enforces
    ///     <see cref="OutboxReactionRetryOptions.MaxAttempts" /> for it, so a policy that always retries cannot
    ///     hold a lease forever. Calling this after <see cref="UseReactionRetryPolicyInstance" />, or twice with
    ///     different policies, keeps the latest registration, as every other single-value builder setting does.
    /// </remarks>
    public OutboxModuleBuilder UseReactionRetryPolicy<TPolicy>(InstanceLifetime lifetime = InstanceLifetime.Singleton)
        where TPolicy : class, IOutboxReactionRetryPolicy
    {
        _reactionRetryPolicy = typeof(TPolicy);
        _reactionRetryPolicyLifetime = lifetime;
        return this;
    }

    /// <summary>
    ///     Registers a pre-created policy that decides whether a conflicted reaction publishes again.
    /// </summary>
    /// <param name="policy">The policy instance, shared by every dispatch for the life of the process.</param>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     The overload to reach for when the policy is configured rather than injected. Calling this after
    ///     <see cref="UseReactionRetryPolicy{TPolicy}" />, or twice with different policies, keeps the latest
    ///     registration, as every other single-value builder setting does.
    /// </remarks>
    public OutboxModuleBuilder UseReactionRetryPolicyInstance(IOutboxReactionRetryPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        _reactionRetryPolicy = policy;
        _reactionRetryPolicyLifetime = InstanceLifetime.Singleton;
        return this;
    }

    /// <summary>
    ///     Registers the contract resolver used to select the CLR type for contract lookup during enqueue.
    /// </summary>
    /// <param name="resolver">The resolver that overrides contract lookup type selection.</param>
    /// <returns>The current builder.</returns>
    public OutboxModuleBuilder UseContractResolver(IMessageContractResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        _contractResolver = resolver;
        return this;
    }

    /// <summary>
    ///     Collects the contract resolver registered on this builder, if any.
    /// </summary>
    /// <returns>The registered resolver, or <see langword="null" /> when none was registered.</returns>
    internal IMessageContractResolver? CollectContractResolver()
    {
        return _contractResolver;
    }

    /// <summary>
    ///     Collects the reaction retry policy registered on this builder, if any.
    /// </summary>
    /// <returns>
    ///     The policy as an implementation type or a pre-created instance, or <see langword="null" /> when none was
    ///     registered.
    /// </returns>
    internal object? CollectReactionRetryPolicy()
    {
        return _reactionRetryPolicy;
    }

    /// <summary>
    ///     Collects the lifetime a reaction retry policy implementation type is registered with.
    /// </summary>
    /// <returns>The lifetime supplied to <see cref="UseReactionRetryPolicy{TPolicy}" />.</returns>
    internal InstanceLifetime CollectReactionRetryPolicyLifetime()
    {
        return _reactionRetryPolicyLifetime;
    }

    /// <summary>
    ///     Registers the storage sub-module. Exactly one storage module must be
    ///     registered. Called by extension methods such as UsePostgreSqlStorage().
    /// </summary>
    /// <param name="storageModule">The storage module to register as a child of the outbox module.</param>
    /// <returns>The current builder.</returns>
    public OutboxModuleBuilder RegisterStorage(IOutboxStorageModule storageModule)
    {
        ArgumentNullException.ThrowIfNull(storageModule);

        if (_storageModule is not null)
        {
            throw new DurableStorageConfigurationException(
                "Outbox storage is already configured. " +
                "Call only one of UsePostgreSqlStorage, UseEntityFrameworkCoreStorage, " +
                "or UseInMemoryStorage.");
        }

        _storageModule = storageModule;
        return this;
    }

    /// <summary>
    ///     Registers the dispatcher sub-module. Exactly one dispatcher must be
    ///     registered. Called by extension methods such as UseInProcessDispatch().
    /// </summary>
    /// <param name="dispatcherModule">The dispatcher module to register as a child of the outbox module.</param>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     <see cref="ProcessorOptions" /> keeps an explicit <see cref="ProcessorOptions.HookFailurePolicy" /> value and
    ///     otherwise resolves the dispatcher recommendation after configuration, independent of registration order. A
    ///     caller-set value wins. Leaving the policy unset keeps transport dispatchers on
    ///     <see cref="ProcessorHookFailurePolicy.CompleteDespiteHookFailure" />; overriding it with
    ///     <see cref="ProcessorHookFailurePolicy.DeadLetter" /> dead-letters a message the broker already accepted,
    ///     so the message is re-leased after replay and published a second time.
    /// </remarks>
    public OutboxModuleBuilder RegisterDispatcher(IOutboxDispatcherModule dispatcherModule)
    {
        ArgumentNullException.ThrowIfNull(dispatcherModule);

        if (_dispatcherModule is not null)
        {
            throw new DurableStorageConfigurationException(
                "Outbox dispatcher is already configured. " +
                "Call only one outbox dispatcher registration method such as UseInProcessDispatch or a broker-specific Use*Dispatch extension.");
        }

        _dispatcherModule = dispatcherModule;
        return this;
    }

    /// <summary>
    ///     Registers payload encryption for outbox acceptance and dispatch.
    /// </summary>
    /// <param name="encryptor">The encryptor used before store writes and after store reads.</param>
    /// <returns>The current builder.</returns>
    public OutboxModuleBuilder UsePayloadEncryption(IPayloadEncryptor encryptor)
    {
        ArgumentNullException.ThrowIfNull(encryptor);

        _payloadEncryptor = encryptor;
        return this;
    }

    /// <summary>
    ///     Collects the configured payload encryptor, if any.
    /// </summary>
    /// <returns>The encryptor registered on this builder.</returns>
    internal IPayloadEncryptor? CollectPayloadEncryptor()
    {
        return _payloadEncryptor;
    }

    /// <summary>
    ///     Registers the default scoped domain event collector and stages collected events into the transactional outbox when a mediation completes successfully.
    /// </summary>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     <para>
    ///         Handlers record consequences through <see cref="IDomainEventCollector" /> instead of publishing them
    ///         inline. A completion handler at <see cref="HandlerPriorities.Persistence" /> drains the collector into
    ///         <see cref="ITransactionalOutbox" /> on <see cref="MediationOutcome.Succeeded" /> only, ordered before an
    ///         application's commit at <see cref="HandlerPriorities.UnitOfWork" />, so the staged rows join the unit of
    ///         work that produced them. A failed mediation stages nothing.
    ///     </para>
    ///     <para>
    ///         The application must register a scoped <see cref="ITransactionalOutbox" />, including the Entity Framework
    ///         Core interceptor non-generic adapter over <see cref="ITransactionalOutbox{TContext}" />; the staging handler
    ///         resolves it lazily and only when the drain is non-empty, so mediations that collect nothing never touch it.
    ///         When <see cref="UseAmbientTrace" /> is also enabled, staged envelopes stamp
    ///         <see cref="LiteBus.Messaging.Abstractions.DurableMessaging.MessageTrace.FromAmbient" />. When
    ///         <see cref="UseAmbientTenant" /> is also enabled, staged envelopes stamp the ambient execution identity
    ///         tenant.
    ///     </para>
    /// </remarks>
    public OutboxModuleBuilder StageCollectedEvents()
    {
        _stageCollectedEvents = true;
        return this;
    }

    /// <summary>
    ///     Configures which tracked entities the storage harvesters read when they stage domain events.
    /// </summary>
    /// <typeparam name="TAggregate">The aggregate type the accessors read.</typeparam>
    /// <param name="events">Reads the buffered domain events of one aggregate instance.</param>
    /// <param name="clear">Removes the buffered domain events of one aggregate instance after harvesting.</param>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     <para>
    ///         Aggregates that implement <see cref="IHasDomainEvents" /> are harvested by default once a storage
    ///         harvester is registered, without calling this method. Call it for aggregate shapes with their own event
    ///         storage, so the harvesters can read and clear them.
    ///     </para>
    ///     <para>
    ///         Registering the same aggregate type twice replaces the previous pair, so repeated composition keeps the
    ///         latest configuration. A configured accessor wins over the <see cref="IHasDomainEvents" /> default for
    ///         its aggregate type.
    ///     </para>
    /// </remarks>
    public OutboxModuleBuilder HarvestDomainEvents<TAggregate>(Func<TAggregate, IEnumerable<object>> events, Action<TAggregate> clear)
        where TAggregate : class
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(clear);

        var accessor = DomainEventAccessor.Create(events, clear);

        for (var index = 0; index < _domainEventAccessors.Count; index++)
        {
            if (_domainEventAccessors[index].AggregateType == typeof(TAggregate))
            {
                _domainEventAccessors[index] = accessor;
                return this;
            }
        }

        _domainEventAccessors.Add(accessor);
        return this;
    }

    /// <summary>
    ///     Configures which tracked entities the storage harvesters read when they stage domain events, for an aggregate type given as a <see cref="Type" />.
    /// </summary>
    /// <param name="aggregateType">The aggregate type the accessors read, either closed or an open generic definition such as <c>typeof(AggregateRoot&lt;&gt;)</c>.</param>
    /// <param name="events">Reads the buffered domain events of one aggregate instance.</param>
    /// <param name="clear">Removes the buffered domain events of one aggregate instance after harvesting.</param>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     <para>
    ///         A closed type behaves like the generic overload: the entity type itself and anything derived from or
    ///         implementing it harvest through the accessors. An open generic definition matches every closed entity
    ///         built on it, found up the base-type chain and among the implemented interfaces, so one registration
    ///         covers every aggregate built on the same base without a marker interface.
    ///     </para>
    ///     <para>
    ///         Registering the same aggregate type twice replaces the previous pair, so repeated composition keeps the
    ///         latest configuration.
    ///     </para>
    /// </remarks>
    public OutboxModuleBuilder HarvestDomainEvents(Type aggregateType, Func<object, IEnumerable<object>> events, Action<object> clear)
    {
        ArgumentNullException.ThrowIfNull(aggregateType);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(clear);

        if (!aggregateType.IsClass && !aggregateType.IsInterface)
        {
            throw new ArgumentException(
                $"Domain event harvesting must use a class or interface aggregate type, but '{aggregateType.FullName}' is neither.",
                nameof(aggregateType));
        }

        var accessor = DomainEventAccessor.Create(aggregateType, events, clear);

        for (var index = 0; index < _domainEventAccessors.Count; index++)
        {
            if (_domainEventAccessors[index].AggregateType == aggregateType)
            {
                _domainEventAccessors[index] = accessor;
                return this;
            }
        }

        _domainEventAccessors.Add(accessor);
        return this;
    }

    /// <summary>
    ///     Configures which tracked entities the storage harvesters read when they stage domain events, for aggregates with no common base selected by a predicate.
    /// </summary>
    /// <param name="predicate">Tests whether the accessors read one tracked entity type.</param>
    /// <param name="events">Reads the buffered domain events of one aggregate instance.</param>
    /// <param name="clear">Removes the buffered domain events of one aggregate instance after harvesting.</param>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     <para>
    ///         Type registrations and predicates share one ordered list. An exact type match wins first; otherwise the
    ///         first registration in order whose test matches is used. Register broad conventions after the specific
    ///         types they must not shadow.
    ///     </para>
    ///     <para>
    ///         Registering the same predicate instance twice replaces the previous pair. Different predicates
    ///         accumulate, and the first match in registration order wins.
    ///     </para>
    /// </remarks>
    public OutboxModuleBuilder HarvestDomainEventsWhere(Func<Type, bool> predicate, Func<object, IEnumerable<object>> events, Action<object> clear)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(clear);

        var accessor = DomainEventAccessor.CreateWhere(predicate, events, clear);

        for (var index = 0; index < _domainEventAccessors.Count; index++)
        {
            var registered = _domainEventAccessors[index];

            if (registered.AggregateType is null && registered.IsMatch == predicate)
            {
                _domainEventAccessors[index] = accessor;
                return this;
            }
        }

        _domainEventAccessors.Add(accessor);
        return this;
    }

    /// <summary>
    ///     Translates one domain event type into the messages to stage, through a pure function.
    /// </summary>
    /// <typeparam name="TEvent">The closed domain event type this translation handles.</typeparam>
    /// <param name="translate">Maps one domain event to the messages to stage; an empty list stages nothing.</param>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     <para>
    ///         Translation runs inside the staging completion handler, before the unit of work commits and inside the
    ///         transaction's scope. Translated messages stage through the transactional outbox like collected events;
    ///         they are published messages, so they are staged directly without a further decision.
    ///     </para>
    ///     <para>
    ///         A domain event type declares one decision. Registering the same function twice is a no-op; a different
    ///         decision for the same event type fails composition and names both.
    ///     </para>
    /// </remarks>
    public OutboxModuleBuilder TranslateDomainEvent<TEvent>(Func<TEvent, IReadOnlyList<object>> translate)
        where TEvent : notnull
    {
        ArgumentNullException.ThrowIfNull(translate);

        var registration = DomainEventTranslatorRegistration.ForFunction(translate);

        ThrowIfDomainEventDecided(typeof(TEvent), registration.Description, null, translate);

        _domainEventTranslators[typeof(TEvent)] = registration;
        return this;
    }

    /// <summary>
    ///     Translates one domain event type into the messages to stage, through a translator resolved from the dispatch scope.
    /// </summary>
    /// <typeparam name="TEvent">The closed domain event type this translation handles.</typeparam>
    /// <typeparam name="TTranslator">The translator type resolved from the dispatch scope.</typeparam>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     <para>
    ///         Translation runs inside the staging completion handler, before the unit of work commits and inside the
    ///         transaction's scope, with scoped services available through
    ///         <see cref="DomainEventTranslationContext.ServiceProvider" />. Translated messages stage through the
    ///         transactional outbox like collected events; they are published messages, so they are staged directly
    ///         without a further decision.
    ///     </para>
    ///     <para>
    ///         A domain event type declares one decision. Registering the same translator twice is a no-op; a different
    ///         decision for the same event type fails composition and names both.
    ///     </para>
    /// </remarks>
    public OutboxModuleBuilder TranslateDomainEvent<TEvent, TTranslator>()
        where TEvent : notnull
        where TTranslator : class, IDomainEventTranslator<TEvent>
    {
        var registration = DomainEventTranslatorRegistration.ForTranslator<TEvent, TTranslator>();

        ThrowIfDomainEventDecided(typeof(TEvent), registration.Description, typeof(TTranslator));

        _domainEventTranslators[typeof(TEvent)] = registration;
        return this;
    }

    /// <summary>
    ///     Drops one domain event type without staging it.
    /// </summary>
    /// <typeparam name="TEvent">The closed domain event type to drop.</typeparam>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     <para>
    ///         Ignored events never reach the transactional outbox and need no contract registration. Use this for
    ///         events consumed inside the mediation, such as by a post-handler, that must never be published.
    ///     </para>
    ///     <para>
    ///         A domain event type declares one decision. Ignoring the same event type twice is a no-op; a different
    ///         decision for the same event type fails composition and names both.
    ///     </para>
    /// </remarks>
    public OutboxModuleBuilder IgnoreDomainEvent<TEvent>()
        where TEvent : notnull
    {
        var eventType = DomainEventTranslatorRegistration.ThrowIfOpenEventType(typeof(TEvent));

        ThrowIfDomainEventDecided(eventType, $"IgnoreDomainEvent<{eventType.FullName}>");

        _ignoredDomainEvents.Add(eventType);
        return this;
    }

    /// <summary>
    ///     Publishes one domain event type in process through the event mediator after the commit, as a root mediation with a cause.
    /// </summary>
    /// <typeparam name="TEvent">The closed domain event type to publish in process.</typeparam>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     <para>
    ///         The event is recorded during staging and published after the unit of work commits, as a root mediation
    ///         whose cause identifies the domain event. A mediation with no unit of work has no commit to wait for, so
    ///         the event publishes when staging completes instead. There is deliberately no pre-commit in-process
    ///         dispatch: reactions that must join the transaction already exist as post-handlers.
    ///     </para>
    ///     <para>
    ///         A domain event type declares one decision. Publishing the same event type in process twice is a no-op;
    ///         a different decision for the same event type fails composition and names both.
    ///     </para>
    /// </remarks>
    public OutboxModuleBuilder PublishDomainEventInProcess<TEvent>()
        where TEvent : notnull
    {
        var eventType = DomainEventTranslatorRegistration.ThrowIfOpenEventType(typeof(TEvent));

        ThrowIfDomainEventDecided(eventType, $"PublishDomainEventInProcess<{eventType.FullName}>");

        _inProcessDomainEvents.Add(eventType);
        return this;
    }

    /// <summary>
    ///     Stages one domain event type unchanged, in its collected shape.
    /// </summary>
    /// <typeparam name="TEvent">The closed domain event type to stage unchanged.</typeparam>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     <para>
    ///         Staged events reach the transactional outbox in their collected shape, so the event type needs a
    ///         contract registration. Use this for events whose collected shape is already the published contract,
    ///         so the decision reads as intent instead of an identity translation. A staged event type counts as
    ///         decided under <see cref="RequireDomainEventDecision" />.
    ///     </para>
    ///     <para>
    ///         A domain event type declares one decision. Staging the same event type twice is a no-op; a different
    ///         decision for the same event type fails composition and names both.
    ///     </para>
    /// </remarks>
    public OutboxModuleBuilder StageDomainEvent<TEvent>()
        where TEvent : notnull
    {
        var eventType = DomainEventTranslatorRegistration.ThrowIfOpenEventType(typeof(TEvent));

        ThrowIfDomainEventDecided(eventType, $"StageDomainEvent<{eventType.FullName}>");

        _stagedDomainEvents.Add(eventType);
        return this;
    }

    /// <summary>
    ///     Fails staging before the commit when a drained domain event has no registered decision.
    /// </summary>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     Harvested types are known only at run time, so a composition check cannot enumerate them. The staging
    ///     handler throws instead, before the commit, naming the event type and the missing decision, so the business
    ///     write never commits without its declared consequences.
    /// </remarks>
    public OutboxModuleBuilder RequireDomainEventDecision()
    {
        _undecidedDomainEventPolicy = UndecidedDomainEventPolicy.Throw;
        return this;
    }

    /// <summary>
    ///     Throws when one domain event type already declares a different decision.
    /// </summary>
    /// <param name="eventType">The closed domain event type one builder call registers.</param>
    /// <param name="decision">The builder call shape of the new decision.</param>
    /// <param name="translatorType">The translator type of the new decision, or <see langword="null" /> when it translates through a function or is not a translation.</param>
    /// <param name="translateFunction">The translation function of the new decision, or <see langword="null" /> when it translates through a type or is not a translation.</param>
    /// <remarks>
    ///     An identical repeat is a no-op and returns silently. A conflicting repeat fails composition and names both
    ///     decisions. Two translation functions are identical only when they are the same delegate instance; the
    ///     library cannot prove two different instances translate the same way, so they conflict.
    /// </remarks>
    private void ThrowIfDomainEventDecided(Type eventType, string decision, Type? translatorType = null, Delegate? translateFunction = null)
    {
        if (_domainEventTranslators.TryGetValue(eventType, out var translator))
        {
            if (string.Equals(translator.Description, decision, StringComparison.Ordinal) &&
                translator.TranslatorType == translatorType &&
                Equals(translator.TranslateFunction, translateFunction))
            {
                return;
            }

            throw new LiteBusConfigurationException(
                $"Domain event '{eventType.FullName}' already declares {translator.Description}, so {decision} conflicts with it. " +
                "Declare one decision per domain event type: translate, ignore, publish in process, or stage.");
        }

        if (_ignoredDomainEvents.Contains(eventType))
        {
            var existing = $"IgnoreDomainEvent<{eventType.FullName}>";

            if (string.Equals(existing, decision, StringComparison.Ordinal))
            {
                return;
            }

            throw new LiteBusConfigurationException(
                $"Domain event '{eventType.FullName}' already declares {existing}, so {decision} conflicts with it. " +
                "Declare one decision per domain event type: translate, ignore, publish in process, or stage.");
        }

        if (_inProcessDomainEvents.Contains(eventType))
        {
            var existing = $"PublishDomainEventInProcess<{eventType.FullName}>";

            if (string.Equals(existing, decision, StringComparison.Ordinal))
            {
                return;
            }

            throw new LiteBusConfigurationException(
                $"Domain event '{eventType.FullName}' already declares {existing}, so {decision} conflicts with it. " +
                "Declare one decision per domain event type: translate, ignore, publish in process, or stage.");
        }

        if (_stagedDomainEvents.Contains(eventType))
        {
            var existing = $"StageDomainEvent<{eventType.FullName}>";

            if (string.Equals(existing, decision, StringComparison.Ordinal))
            {
                return;
            }

            throw new LiteBusConfigurationException(
                $"Domain event '{eventType.FullName}' already declares {existing}, so {decision} conflicts with it. " +
                "Declare one decision per domain event type: translate, ignore, publish in process, or stage.");
        }
    }

    /// <summary>
    ///     Stamps trace metadata from the ambient execution context onto enqueued envelopes whose items carry none.
    /// </summary>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     <para>
    ///         When an item's trace is
    ///         <see cref="LiteBus.Messaging.Abstractions.DurableMessaging.MessageTrace.None" />, the envelope factory
    ///         stores <see cref="LiteBus.Messaging.Abstractions.DurableMessaging.MessageTrace.FromAmbient" /> instead.
    ///         An explicit item trace always wins over the ambient value, and outside a mediation the ambient read
    ///         yields none, so the envelope is identical to one enqueued without this option.
    ///     </para>
    /// </remarks>
    public OutboxModuleBuilder UseAmbientTrace()
    {
        _useAmbientTrace = true;
        return this;
    }

    /// <summary>
    ///     Stamps the ambient execution identity tenant onto enqueued envelopes whose items carry no explicit tenant.
    /// </summary>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     <para>
    ///         When an item's tenant is
    ///         <see cref="LiteBus.Messaging.Abstractions.DurableMessaging.TenantScope.Unscoped" />, the envelope factory
    ///         stores the tenant of <see cref="AmbientExecutionContext" /> instead. An explicit item tenant in
    ///         <see cref="LiteBus.Messaging.Abstractions.DurableMessaging.TenantScope.Isolated" /> always wins over the
    ///         ambient value, and outside a mediation the ambient read yields none, so the envelope is identical to one
    ///         enqueued without this option.
    ///     </para>
    /// </remarks>
    public OutboxModuleBuilder UseAmbientTenant()
    {
        _useAmbientTenant = true;
        return this;
    }

    /// <summary>
    ///     Writes one envelope per subscribed main handler of each enqueued event, so every reaction gets its own retry, dead letter, and replay.
    /// </summary>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     <para>
    ///         Each envelope records the full name of the handler it is addressed to, and in-process dispatch publishes
    ///         each envelope to that handler alone. Enqueueing an event with no subscribed main handler throws
    ///         <see cref="NoHandlerFoundException" />, matching the default in-process dispatch rule that treats an
    ///         unhandled durable message as a failure.
    ///     </para>
    ///     <para>
    ///         The trade-off is storage: one row per handler instead of one row per event. A handler added after enqueue
    ///         does not receive already-enqueued events, and a handler removed before dispatch leaves its envelopes with
    ///         no matching subscriber, which the dispatch <c>RequireHandler</c> option then decides. Use this only with
    ///         in-process dispatch: broker dispatchers publish the full event per envelope, so per-handler envelopes
    ///         would duplicate broker publication. Composition throws <see cref="DurableStorageConfigurationException" />
    ///         when per-handler delivery meets a transport dispatcher unless
    ///         <see cref="AllowPerHandlerDeliveryWithTransportDispatch" /> acknowledges the duplication.
    ///     </para>
    /// </remarks>
    public OutboxModuleBuilder EnablePerHandlerDelivery()
    {
        _perHandlerDelivery = true;
        return this;
    }

    /// <summary>
    ///     Acknowledges that per-handler envelopes duplicate broker publication when combined with a transport dispatcher.
    /// </summary>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     Call this only when the duplicate publication is intended, for example during a migration that replays
    ///     per-handler rows through a broker. Without this acknowledgement, composition throws
    ///     <see cref="DurableStorageConfigurationException" /> when <see cref="EnablePerHandlerDelivery" /> meets a
    ///     transport dispatcher.
    /// </remarks>
    public OutboxModuleBuilder AllowPerHandlerDeliveryWithTransportDispatch()
    {
        _allowPerHandlerDeliveryWithTransportDispatch = true;
        return this;
    }

    /// <summary>
    ///     Dispatches the outbox envelopes a mediation staged right after its unit of work commits, for handlers or
    ///     events marked with <see cref="DispatchImmediatelyAttribute" />, with the outbox processor as the fallback.
    /// </summary>
    /// <param name="configure">An optional callback that sets the timeout, the fallback delay and the wait mode.</param>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     <para>
    ///         An immediate envelope is written invisible to the outbox processor for
    ///         <see cref="OutboxImmediateDispatchOptions.FallbackDelay" />. After the commit, LiteBus leases exactly the
    ///         envelopes the mediation staged and dispatches them through the registered dispatcher, with the processor's
    ///         success, failure and retry rules. A successful dispatch marks the envelope published, so each healthy
    ///         handler runs once. A failed one is retried by the processor, and the command still succeeds.
    ///     </para>
    ///     <para>
    ///         Composition requires a registered unit of work, a dispatcher, and a store that implements
    ///         <see cref="IOutboxTargetedLeaseStore" />, and throws <see cref="LiteBusConfigurationException" /> otherwise.
    ///     </para>
    /// </remarks>
    public OutboxModuleBuilder EnableImmediateDispatch(Action<OutboxImmediateDispatchOptions>? configure = null)
    {
        _immediateDispatch ??= new OutboxImmediateDispatchOptions();
        configure?.Invoke(_immediateDispatch);
        return this;
    }

    /// <summary>
    ///     Gets the immediate dispatch options, or <see langword="null" /> when <see cref="EnableImmediateDispatch" />
    ///     was not called.
    /// </summary>
    internal OutboxImmediateDispatchOptions? ImmediateDispatchOptions => _immediateDispatch;

    /// <summary>
    ///     Gets a value indicating whether <see cref="StageCollectedEvents" /> was called.
    /// </summary>
    internal bool IsCollectedEventStagingEnabled => _stageCollectedEvents;

    /// <summary>
    ///     Gets a value indicating whether <see cref="AllowPerHandlerDeliveryWithTransportDispatch" /> was called.
    /// </summary>
    internal bool IsPerHandlerDeliveryWithTransportDispatchAllowed => _allowPerHandlerDeliveryWithTransportDispatch;

    /// <summary>
    ///     Gets a value indicating whether <see cref="EnablePerHandlerDelivery" /> was called.
    /// </summary>
    internal bool IsPerHandlerDeliveryEnabled => _perHandlerDelivery;

    /// <summary>
    ///     Gets the dispatcher sub-module registered on this builder, if any.
    /// </summary>
    internal IOutboxDispatcherModule? DispatcherModule => _dispatcherModule;

    /// <summary>
    ///     Collects the enqueue options built from the ambient trace, ambient tenant, and per-handler delivery flags.
    /// </summary>
    /// <returns>The enqueue options the module registers.</returns>
    internal OutboxEnqueueOptions CollectEnqueueOptions()
    {
        return new OutboxEnqueueOptions
        {
            UseAmbientTrace = _useAmbientTrace,
            UseAmbientTenant = _useAmbientTenant,
            PerHandlerDelivery = _perHandlerDelivery
        };
    }

    /// <summary>
    ///     Registers a consumer-owned diagnostic probe for this outbox.
    /// </summary>
    /// <typeparam name="TCheck">The probe type that implements <see cref="IDiagnosticCheck" />.</typeparam>
    /// <param name="name">The probe name reported to operators and health hosts.</param>
    /// <returns>The current builder.</returns>
    public OutboxModuleBuilder AddDiagnosticCheck<TCheck>(string name)
        where TCheck : IDiagnosticCheck
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _diagnosticChecks.Add(new DiagnosticCheckRegistration(typeof(TCheck), name));
        return this;
    }

    /// <summary>
    ///     Collects consumer-owned diagnostic probes registered on this builder.
    /// </summary>
    /// <returns>The diagnostic probe registrations declared on this builder.</returns>
    internal IReadOnlyList<DiagnosticCheckRegistration> CollectDiagnosticChecks()
    {
        return _diagnosticChecks;
    }

    /// <summary>
    ///     Collects the configured domain event accessor pairs in registration order.
    /// </summary>
    /// <returns>The accessor pairs declared on this builder.</returns>
    internal IReadOnlyList<DomainEventAccessor> CollectDomainEventAccessors()
    {
        return [.. _domainEventAccessors];
    }

    /// <summary>
    ///     Collects the per-type domain event decisions declared on this builder.
    /// </summary>
    /// <returns>The decisions the staging completion handler reads.</returns>
    internal DomainEventDecisions CollectDomainEventDecisions()
    {
        return new DomainEventDecisions(
            new Dictionary<Type, DomainEventTranslatorRegistration>(_domainEventTranslators),
            new HashSet<Type>(_ignoredDomainEvents),
            new HashSet<Type>(_inProcessDomainEvents),
            new HashSet<Type>(_stagedDomainEvents),
            _undecidedDomainEventPolicy);
    }

    /// <summary>
    ///     Collects the translator types declared on this builder, so the outbox module can register them in dependency injection.
    /// </summary>
    /// <returns>The translator types declared through the translator overload, in no particular order.</returns>
    internal IReadOnlyList<Type> CollectDomainEventTranslatorTypes()
    {
        List<Type> translatorTypes = [];

        foreach (var registration in _domainEventTranslators.Values)
        {
            if (registration.TranslatorType is not null)
            {
                translatorTypes.Add(registration.TranslatorType);
            }
        }

        return translatorTypes;
    }

    /// <summary>
    ///     Collects configured sub-modules in storage then dispatcher order.
    /// </summary>
    /// <returns>The sub-modules declared on this builder.</returns>
    public IReadOnlyList<IModule> CollectSubModules()
    {
        return (_storageModule, _dispatcherModule) switch
        {
            (not null, not null) => [_storageModule, _dispatcherModule],
            (not null, null) => [_storageModule],
            (null, not null) => [_dispatcherModule],
            _ => []
        };
    }

    /// <summary>
    ///     Applies deferred contract registrations to the live registry.
    /// </summary>
    /// <param name="registry">The shared message contract registry.</param>
    public void ApplyContracts(IMessageContractRegistry registry)
    {
        _contracts.ApplyTo(registry);
    }
}
