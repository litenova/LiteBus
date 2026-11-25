using LiteBus.Events;
using LiteBus.Events.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Abstractions.Exceptions;
using LiteBus.Outbox.Dispatch.InProcess;
using LiteBus.Outbox.Storage.InMemory;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Outbox.UnitTests.Dispatch.InProcess;

[Collection("Sequential")]
public sealed class OutboxReactionRetryTests : LiteBusTestBase
{
    private const string ContractName = "tests.events.reaction";

    /// <summary>
    ///     Verifies a conflicted reaction publishes again inside the same dispatch without consuming a store attempt.
    /// </summary>
    [Fact]
    public async Task Processor_should_retry_conflicted_reaction_in_dispatch_and_publish()
    {
        var script = new ConflictScript { FailuresRemaining = 1 };

        using var serviceProvider = CreateProvider(
            script,
            builder => builder.UseReactionRetry(options =>
            {
                options.InitialDelay = TimeSpan.FromMilliseconds(1);
                options.UseJitter = false;
            }));

        var outbox = serviceProvider.GetRequiredService<IOutbox>();
        var processor = serviceProvider.GetRequiredService<IOutboxProcessor>();
        var messageId = await EnqueueAsync(outbox).ConfigureAwait(false);

        await processor.ProcessPendingAsync().ConfigureAwait(false);

        var envelope = serviceProvider.GetRequiredService<InMemoryOutboxStore>().Get(messageId);
        envelope.Status.Should().Be(OutboxStatus.Published);
        envelope.AttemptCount.Should().Be(1);
        script.Runs.Should().Be(2);
        script.ObservedDispatches.Should().HaveCount(2);

        foreach (var dispatch in script.ObservedDispatches)
        {
            dispatch.Should().NotBeNull();
            dispatch!.EnvelopeId.Should().Be(messageId);
            dispatch.ContractName.Should().Be(ContractName);
            dispatch.AttemptNumber.Should().Be(1);
            dispatch.Kind.Should().Be(OutboxDispatchKind.Background);
            dispatch.Cause.Should().NotBeNull();
        }
    }

    /// <summary>
    ///     Verifies a business refusal retires the envelope on the first attempt without an in-dispatch retry.
    /// </summary>
    [Fact]
    public async Task Processor_should_not_retry_business_refusal_in_dispatch()
    {
        var script = new ConflictScript
        {
            FailuresRemaining = int.MaxValue,
            FailureToThrow = new LiteBusMessageDeniedException(typeof(ReactionTestEvent), "denied", "TEST")
        };

        using var serviceProvider = CreateProvider(script, _ => { });

        var outbox = serviceProvider.GetRequiredService<IOutbox>();
        var processor = serviceProvider.GetRequiredService<IOutboxProcessor>();
        var messageId = await EnqueueAsync(outbox).ConfigureAwait(false);

        await processor.ProcessPendingAsync().ConfigureAwait(false);

        var envelope = serviceProvider.GetRequiredService<InMemoryOutboxStore>().Get(messageId);
        envelope.Status.Should().Be(OutboxStatus.DeadLettered);
        script.Runs.Should().Be(1);
    }

    /// <summary>
    ///     Verifies a configuration fault follows the ordinary envelope schedule without an in-dispatch retry.
    /// </summary>
    [Fact]
    public async Task Processor_should_not_retry_configuration_fault_in_dispatch()
    {
        var script = new ConflictScript
        {
            FailuresRemaining = int.MaxValue,
            FailureToThrow = new MessageFaultException(typeof(ReactionTestEvent), "TEST", "misconfigured")
        };

        using var serviceProvider = CreateProvider(script, _ => { });

        var outbox = serviceProvider.GetRequiredService<IOutbox>();
        var processor = serviceProvider.GetRequiredService<IOutboxProcessor>();
        var messageId = await EnqueueAsync(outbox).ConfigureAwait(false);

        await processor.ProcessPendingAsync().ConfigureAwait(false);

        var envelope = serviceProvider.GetRequiredService<InMemoryOutboxStore>().Get(messageId);
        envelope.Status.Should().Be(OutboxStatus.Failed);
        script.Runs.Should().Be(1);
    }

    /// <summary>
    ///     Verifies bounded in-dispatch attempts end the dispatch and the envelope follows the ordinary schedule.
    /// </summary>
    [Fact]
    public async Task Processor_should_exhaust_bounded_attempts_and_follow_envelope_schedule()
    {
        var script = new ConflictScript { FailuresRemaining = int.MaxValue };

        using var serviceProvider = CreateProvider(
            script,
            builder => builder.UseReactionRetry(options =>
            {
                options.MaxAttempts = 2;
                options.InitialDelay = TimeSpan.FromMilliseconds(1);
                options.UseJitter = false;
            }));

        var outbox = serviceProvider.GetRequiredService<IOutbox>();
        var processor = serviceProvider.GetRequiredService<IOutboxProcessor>();
        var messageId = await EnqueueAsync(outbox).ConfigureAwait(false);

        await processor.ProcessPendingAsync().ConfigureAwait(false);

        var envelope = serviceProvider.GetRequiredService<InMemoryOutboxStore>().Get(messageId);
        envelope.Status.Should().Be(OutboxStatus.Failed);
        script.Runs.Should().Be(2);
    }

    /// <summary>
    ///     Verifies a replacement policy is consulted with the failure, the delivery, and the evaluation.
    /// </summary>
    [Fact]
    public async Task Processor_should_consult_custom_policy_with_failure_and_delivery()
    {
        var script = new ConflictScript { FailuresRemaining = int.MaxValue };
        var policy = new RecordingRetryPolicy();

        using var serviceProvider = CreateProvider(
            script,
            builder => builder.UseReactionRetryPolicyInstance(policy));

        var outbox = serviceProvider.GetRequiredService<IOutbox>();
        var processor = serviceProvider.GetRequiredService<IOutboxProcessor>();
        var messageId = await EnqueueWithTenantAsync(outbox, "tenant-a").ConfigureAwait(false);

        await processor.ProcessPendingAsync().ConfigureAwait(false);

        var envelope = serviceProvider.GetRequiredService<InMemoryOutboxStore>().Get(messageId);
        envelope.Status.Should().Be(OutboxStatus.Failed);
        script.Runs.Should().Be(1);
        policy.Evaluations.Should().ContainSingle();

        var evaluation = policy.Evaluations[0];
        evaluation.ReactionAttempt.Should().Be(1);
        evaluation.Failure.Should().BeOfType<TestConcurrencyException>();
        evaluation.Dispatch.EnvelopeId.Should().Be(messageId);
        evaluation.Dispatch.ContractName.Should().Be(ContractName);
        evaluation.Dispatch.AttemptNumber.Should().Be(1);
        evaluation.Dispatch.Kind.Should().Be(OutboxDispatchKind.Background);
        evaluation.Dispatch.Identity.TenantId.Should().Be("tenant-a");
        evaluation.Evaluation.Message.Should().BeOfType<ReactionTestEvent>();
        evaluation.Evaluation.Identity.TenantId.Should().Be("tenant-a");
        evaluation.Evaluation.Services.Should().NotBeNull();
    }

    /// <summary>
    ///     Verifies a disabled retry keeps conflicts on the ordinary envelope schedule.
    /// </summary>
    [Fact]
    public async Task Processor_should_skip_retry_when_disabled()
    {
        var script = new ConflictScript { FailuresRemaining = int.MaxValue };

        using var serviceProvider = CreateProvider(
            script,
            builder => builder.UseReactionRetry(options => options.Enabled = false));

        var outbox = serviceProvider.GetRequiredService<IOutbox>();
        var processor = serviceProvider.GetRequiredService<IOutboxProcessor>();
        var messageId = await EnqueueAsync(outbox).ConfigureAwait(false);

        await processor.ProcessPendingAsync().ConfigureAwait(false);

        var envelope = serviceProvider.GetRequiredService<InMemoryOutboxStore>().Get(messageId);
        envelope.Status.Should().Be(OutboxStatus.Failed);
        script.Runs.Should().Be(1);
    }

    /// <summary>
    ///     Verifies a throwing policy ends the dispatch without losing the conflicted envelope.
    /// </summary>
    [Fact]
    public async Task Processor_should_follow_envelope_schedule_when_policy_throws()
    {
        var script = new ConflictScript { FailuresRemaining = int.MaxValue };
        var policy = new RecordingRetryPolicy { Failure = new InvalidOperationException("policy bug") };

        using var serviceProvider = CreateProvider(
            script,
            builder => builder.UseReactionRetryPolicyInstance(policy));

        var outbox = serviceProvider.GetRequiredService<IOutbox>();
        var processor = serviceProvider.GetRequiredService<IOutboxProcessor>();
        var messageId = await EnqueueAsync(outbox).ConfigureAwait(false);

        await processor.ProcessPendingAsync().ConfigureAwait(false);

        var envelope = serviceProvider.GetRequiredService<InMemoryOutboxStore>().Get(messageId);
        envelope.Status.Should().Be(OutboxStatus.Failed);
        script.Runs.Should().Be(1);
        policy.Evaluations.Should().BeEmpty();
    }

    /// <summary>
    ///     Verifies a conflict carrying a configuration fault never reaches the policy.
    /// </summary>
    [Fact]
    public async Task Processor_should_not_consult_policy_when_conflict_carries_fault()
    {
        var script = new ConflictScript
        {
            FailuresRemaining = int.MaxValue,
            FailureToThrow = new TestConcurrencyException(
                new MessageFaultException(typeof(ReactionTestEvent), "TEST", "misconfigured"))
        };
        var policy = new RecordingRetryPolicy();

        using var serviceProvider = CreateProvider(
            script,
            builder => builder.UseReactionRetryPolicyInstance(policy));

        var outbox = serviceProvider.GetRequiredService<IOutbox>();
        var processor = serviceProvider.GetRequiredService<IOutboxProcessor>();
        var messageId = await EnqueueAsync(outbox).ConfigureAwait(false);

        await processor.ProcessPendingAsync().ConfigureAwait(false);

        var envelope = serviceProvider.GetRequiredService<InMemoryOutboxStore>().Get(messageId);
        envelope.Status.Should().Be(OutboxStatus.Failed);
        script.Runs.Should().Be(1);
        policy.Evaluations.Should().BeEmpty();
    }

    /// <summary>
    ///     Verifies immediate delivery stamps the immediate kind and retries through the default policy.
    /// </summary>
    [Fact]
    public async Task Direct_dispatch_should_stamp_immediate_kind_and_retry_with_default_policy()
    {
        var mediator = new CapturingEventMediator { FailuresRemaining = 1 };
        var contracts = new MessageContractRegistry();
        contracts.Register<ReactionTestEvent>(ContractName);
        var serializer = new SystemTextJsonMessageSerializer();
        var dispatcher = new EventOutboxDispatcher(
            mediator,
            contracts,
            serializer,
            reactionRetryOptions: new OutboxReactionRetryOptions
            {
                InitialDelay = TimeSpan.FromMilliseconds(1),
                UseJitter = false
            });
        var envelope = await CreateEnvelopeAsync(serializer).ConfigureAwait(false);

        await dispatcher.DispatchAsync(envelope, OutboxDispatchKind.Immediate).ConfigureAwait(false);

        mediator.PublishCount.Should().Be(2);
        mediator.LastSettings.Should().NotBeNull();
        OutboxDispatchContext.TryGet(mediator.LastSettings!.Items, out var dispatch).Should().BeTrue();
        var observed = dispatch!;
        observed.EnvelopeId.Should().Be(envelope.Id);
        observed.ContractName.Should().Be(ContractName);
        observed.AttemptNumber.Should().Be(envelope.AttemptCount);
        observed.Kind.Should().Be(OutboxDispatchKind.Immediate);
        observed.Cause.CorrelationId.Should().Be(envelope.CorrelationId);
        observed.Cause.CausationId.Should().Be(envelope.CausationId);
        observed.Identity.TenantId.Should().Be(envelope.TenantId);
    }

    /// <summary>
    ///     Verifies a dispatch without a delivery path reports the background processor kind.
    /// </summary>
    [Fact]
    public async Task Direct_dispatch_should_default_to_background_kind()
    {
        var mediator = new CapturingEventMediator();
        var contracts = new MessageContractRegistry();
        contracts.Register<ReactionTestEvent>(ContractName);
        var serializer = new SystemTextJsonMessageSerializer();
        var dispatcher = new EventOutboxDispatcher(mediator, contracts, serializer);
        var envelope = await CreateEnvelopeAsync(serializer).ConfigureAwait(false);

        await dispatcher.DispatchAsync(envelope).ConfigureAwait(false);

        mediator.PublishCount.Should().Be(1);
        OutboxDispatchContext.TryGet(mediator.LastSettings!.Items, out var dispatch).Should().BeTrue();
        dispatch!.Kind.Should().Be(OutboxDispatchKind.Background);
    }

    /// <summary>
    ///     Verifies a refusal ends a direct dispatch on its first attempt.
    /// </summary>
    [Fact]
    public async Task Direct_dispatch_should_not_retry_refusal()
    {
        var mediator = new CapturingEventMediator
        {
            FailuresRemaining = int.MaxValue,
            FailureToThrow = new LiteBusMessageDeniedException(typeof(ReactionTestEvent), "denied", "TEST")
        };
        var contracts = new MessageContractRegistry();
        contracts.Register<ReactionTestEvent>(ContractName);
        var serializer = new SystemTextJsonMessageSerializer();
        var dispatcher = new EventOutboxDispatcher(mediator, contracts, serializer);
        var envelope = await CreateEnvelopeAsync(serializer).ConfigureAwait(false);

        var act = () => dispatcher.DispatchAsync(envelope, OutboxDispatchKind.Immediate);

        var failure = await act.Should().ThrowAsync<LiteBusDispatchException>().ConfigureAwait(false);
        failure.Which.InnerException.Should().BeOfType<LiteBusMessageDeniedException>();
        mediator.PublishCount.Should().Be(1);
    }

    private static ServiceProvider CreateProvider(ConflictScript script, Action<OutboxModuleBuilder> configure)
    {
        return new ServiceCollection()
            .AddSingleton(script)
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ =>
                {
                });

                registry.AddEvents(builder => builder.Register<ScriptedReactionHandler>());

                registry.AddOutbox(builder =>
                {
                    builder.Contracts.Register<ReactionTestEvent>(ContractName);

                    builder.UseProcessorOptions(new OutboxProcessorOptions
                    {
                        BatchSize = 10,
                        LeaseOwner = "reaction-test",
                        Retry = new RetryOptions { UseJitter = false }
                    });

                    builder.UseInMemoryStorage();
                    builder.UseInProcessDispatch();
                    configure(builder);
                });
            })
            .BuildServiceProvider();
    }

    private static async Task<Guid> EnqueueAsync(IOutbox outbox)
    {
        var messageId = Guid.NewGuid();

        await outbox.EnqueueAsync(
                OutboxEnqueueItem<ReactionTestEvent>.WithIdentity(new ReactionTestEvent(Guid.NewGuid()), messageId))
            .ConfigureAwait(false);

        return messageId;
    }

    private static async Task<Guid> EnqueueWithTenantAsync(IOutbox outbox, string tenantId)
    {
        var messageId = Guid.NewGuid();

        await outbox.EnqueueAsync(
                OutboxEnqueueItem<ReactionTestEvent>.From(
                    new ReactionTestEvent(Guid.NewGuid()),
                    OutboxEnqueueMetadata.Immediate with
                    {
                        Identity = new MessageIdentity.Supplied(messageId),
                        Tenant = new TenantScope.Isolated(tenantId)
                    }))
            .ConfigureAwait(false);

        return messageId;
    }

    private static async Task<OutboxEnvelope> CreateEnvelopeAsync(SystemTextJsonMessageSerializer serializer)
    {
        var payload = await serializer.SerializeAsync(new ReactionTestEvent(Guid.NewGuid())).ConfigureAwait(false);

        return new OutboxEnvelope
        {
            Id = Guid.NewGuid(),
            ContractName = ContractName,
            ContractVersion = 1,
            Payload = payload,
            CreatedAt = DateTimeOffset.UtcNow,
            Status = OutboxStatus.Publishing,
            AttemptCount = 2,
            CorrelationId = "correlation-1",
            CausationId = "causation-1",
            TenantId = "tenant-a"
        };
    }

    private sealed record ReactionTestEvent(Guid OrderId) : IEvent;

    private sealed class TestConcurrencyException : Exception
    {
        public TestConcurrencyException(Exception? inner = null)
            : base("lost race", inner)
        {
        }
    }

    private sealed class ConflictScript
    {
        public int FailuresRemaining;
        public int Runs;
        public Exception FailureToThrow = new TestConcurrencyException();
        public readonly List<OutboxDispatchContext?> ObservedDispatches = new();
    }

    private sealed class ScriptedReactionHandler : IEventHandler<ReactionTestEvent>
    {
        private readonly ConflictScript _script;

        public ScriptedReactionHandler(ConflictScript script)
        {
            ArgumentNullException.ThrowIfNull(script);
            _script = script;
        }

        public Task HandleAsync(ReactionTestEvent message, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _script.Runs);

            var dispatch = OutboxDispatchContext.TryGet(AmbientExecutionContext.Current.Items, out var current)
                ? current
                : null;

            lock (_script.ObservedDispatches)
            {
                _script.ObservedDispatches.Add(dispatch);
            }

            if (Interlocked.Decrement(ref _script.FailuresRemaining) >= 0)
            {
                throw _script.FailureToThrow;
            }

            return Task.CompletedTask;
        }
    }

    private sealed class RecordingRetryPolicy : IOutboxReactionRetryPolicy
    {
        public readonly List<OutboxReactionRetryContext> Evaluations = new();
        public OutboxReactionRetryDecision Answer = new OutboxReactionRetryDecision.GiveUp();
        public Exception? Failure;

        public ValueTask<OutboxReactionRetryDecision> DecideAsync(
            OutboxReactionRetryContext context,
            CancellationToken cancellationToken)
        {
            if (Failure is not null)
            {
                throw Failure;
            }

            lock (Evaluations)
            {
                Evaluations.Add(context);
            }

            return new ValueTask<OutboxReactionRetryDecision>(Answer);
        }
    }

    private sealed class CapturingEventMediator : IEventMediator
    {
        public int FailuresRemaining;
        public int PublishCount;
        public Exception FailureToThrow = new TestConcurrencyException();
        public EventMediationSettings? LastSettings;

        public Task PublishAsync(
            IEvent @event,
            EventMediationSettings? eventMediationSettings = null,
            CancellationToken cancellationToken = default)
        {
            return PublishCore(eventMediationSettings);
        }

        public Task PublishAsync<TEvent>(
            TEvent @event,
            EventMediationSettings? eventMediationSettings = null,
            CancellationToken cancellationToken = default)
            where TEvent : notnull
        {
            return PublishCore(eventMediationSettings);
        }

        private Task PublishCore(EventMediationSettings? settings)
        {
            PublishCount++;
            LastSettings = settings;

            if (Interlocked.Decrement(ref FailuresRemaining) >= 0)
            {
                throw FailureToThrow;
            }

            return Task.CompletedTask;
        }
    }
}
