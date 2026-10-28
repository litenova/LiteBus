# Limits and Opt-in Boundaries

LiteBus mediation runs in the calling process and persists nothing by itself. Durability, transport, and concurrency promises hold only when you register the matching store, dispatcher, or host. This page names each boundary, what it does not do, and what you register when the default is not enough.

> **This page is for** anyone relying on LiteBus durability in production: a developer choosing stores and transports, or an operator reading dashboards and runbooks.
>
> **After reading it** you know which guarantees need explicit registration, where concurrency is capped at one, and where duplicate delivery needs handler-side idempotency. For the inbox contract in full, go on to the [Inbox](../reliable-messaging/inbox.md).

## Mediation Alone Persists Nothing

Handlers execute in the caller process. When the process crashes mid-handler, that work is lost: `SendAsync`, `QueryAsync`, and `PublishAsync` never touch a durable store. See [Reliable Messaging](../reliable-messaging/README.md) for the in-process contract.

When a message has to survive a restart, accept commands through `IInbox.AcceptAsync` and enqueue events through `IOutbox.EnqueueAsync`, with one store adapter and one enabled processor per axis. The receipt confirms storage, not a business result. See [Inbox](../reliable-messaging/inbox.md) and [Outbox](../reliable-messaging/outbox.md).

## One Transport Module per Composition

One composition hosts one transport module. Registering a second broker transport, for example AMQP plus Kafka, fails composition with an error naming both modules, because publishers register as single bindings and metrics carry one broker identity. See [Dependency Graph](../architecture/dependency-graph.md) for package roles.

When a host must reach two brokers, run one host process per broker and bridge them at the application level.

## Concurrency Bounds Differ per Adapter

`Safety.MaxInFlightMessages` caps concurrent LiteBus delivery handlers per subscription, independently from broker receive settings. The enforced bound is 1 for AMQP, Kafka, SQS, and in-memory ingress. On Azure Service Bus the bound is the lower of `Safety.MaxInFlightMessages` and `MaxConcurrentCalls`, which defaults to the SDK value of 1. Raising the value on the other four changes nothing and reports no error. See [Inbox](../reliable-messaging/inbox.md) for the per-adapter receive knobs.

Tune broker receive settings (`PrefetchCount`, `ReceiveBatchSize`, `MaxConcurrentCalls`) per adapter instead. Higher handler concurrency on the capped adapters is roadmap work; see the [Roadmap](../roadmap/README.md).

## Saga Duplicate Suppression Covers the Latest Message

The saga store records only the last applied message ID per correlation. When an inbox terminal persist fails after a save, a newer message for the same correlation can advance the row before the failed message retries, and the retry then runs its handler again. Suppression covers the most recently applied message, not every message the correlation has seen. See [Saga](../reliable-messaging/saga.md).

Handlers with external side effects need their own idempotency on top of this guard. See [Idempotency](../concepts/idempotency.md).

## Lease Visibility Follows the Database Clock

Stores with a database clock use it for lease visibility checks; `InboxLeaseRequest.Now` and `LeaseRenewalRequest.RequestedExpiresAt` are the fallback for stores without one. Application-to-database clock skew larger than a retry delay changes the retry schedule silently. See [Inbox](../reliable-messaging/inbox.md).

Keep host and database clocks synchronized, and keep retry delays larger than the skew you provision for.

## Lease Owner Names Carry a Session Suffix

`ProcessorOptions.LeaseOwner` is a prefix, not the exact recorded name. The processor appends a session suffix that fences instances sharing a configured owner, so a reclaimed row cannot be renewed or completed by a stale processor session. A configured `worker-a` is recorded as `worker-a` followed by a colon and a session value.

Match runbooks and queries on the prefix, not on an exact owner name.

## AMQP Ingress Keys Are Queue-Scoped

Broker ingress deduplicates redeliveries with a broker-scoped key of the form `ingress:{destination}:{brokerMessageId}`. For AMQP, the destination is the queue, so two queues bound to one exchange hold separate keys. See [Inbox](../reliable-messaging/inbox.md).

A redelivery that arrives under a different key than its accepted row creates a second row, and both rows execute. Keep handlers idempotent so a duplicate redelivery is harmless.

## Idempotency Claims Are Separate From the Change

The shipped idempotency stores claim the key on their own connection or session, not inside the handler's transaction. A crash between the claim commit and the handler commit leaves the key claimed for work that never happened, and only a mediation that ends inside the process settles its claim. See [In-Process Idempotency](../concepts/idempotency.md).

An unsettled claim blocks its key until the store's claim timeout passes, five minutes by default, after which a new caller takes the claim over. Keep the timeout longer than the slowest mediation that claims through the store, so a live claimant always settles first. Nothing expires an applied key; purge rows whose repeats stopped being plausible.

## Per-Handler Delivery Trades Rows for Isolated Retry

Under `EnablePerHandlerDelivery`, one enqueue writes one envelope per subscribed handler instead of one envelope per event. A handler added after enqueue never receives those events, because its envelope was never written, and a handler removed before dispatch leaves envelopes with no matching subscriber. Use it only with in-process dispatch: broker dispatchers publish the full event per envelope, so per-handler envelopes would duplicate broker publication. Composition throws `DurableStorageConfigurationException` when per-handler delivery meets a transport dispatcher unless `AllowPerHandlerDeliveryWithTransportDispatch` acknowledges the duplication. See [Outbox](../reliable-messaging/outbox.md#per-handler-delivery).

## The Unit of Work Commits Only Success

`IUnitOfWork.CommitAsync` runs once per mediation that ended `Succeeded`, and never for a denial, a validation failure, a shortcut answer, a fault, or a cancellation. A shortcut answer reports success without committing, because the main handler never ran and staged nothing. LiteBus never opens or owns the transaction; the application opens it and the commit flushes it. A commit that throws fails the mediation with that exception. See [Domain Events and Unit of Work](../reliable-messaging/domain-events-and-unit-of-work.md#command-pipeline-boundary).

## Recurring Single-Flight Has No Standing Leader

Under `RequireLease`, every pass races for the named lease and the winner varies from pass to pass. A holder whose process dies stops renewing, so its pass blocks the loop until `LeaseDuration` lapses; size the duration from how quickly a replacement pass must run after a crash. See [Recurring Commands](../integrations/recurring-commands.md#running-on-several-replicas).

## In-Memory Stores Are Per-Process Doubles

`InMemoryIdempotencyStore` and the in-memory inbox and outbox stores forget everything on restart and share nothing across processes, so two replicas behind a load balancer both apply the same message. Use the PostgreSQL or Marten adapters when an idempotency claim must hold across processes, and the PostgreSQL or Entity Framework Core adapters when durable envelopes must survive a restart. See [In-Process Idempotency](../concepts/idempotency.md).

## No Trimming or NativeAOT Guarantee

Assembly scanning (`RegisterFromAssembly`) and open-generic closing use runtime reflection over handler and message types. Reflection-heavy APIs carry trimming annotations, but LiteBus publishes no trim-safe guarantee today: NativeAOT smoke builds for the mediator, durable, and transport paths are planned work on the [Reliability Roadmap](../roadmap/reliability.md), not a shipped gate.

When trimming matters, register handlers explicitly with `Register<T>` or `Register(typeof(OpenGenericHandler<>))` instead of scanning assemblies, and validate the trimmed application before release.

## Next

- Read the [Inbox](../reliable-messaging/inbox.md) and [Outbox](../reliable-messaging/outbox.md) contracts for the full accept and enqueue paths.
- Read the [Saga](../reliable-messaging/saga.md) contract for correlation and duplicate suppression.
- Read [In-Process Idempotency](../concepts/idempotency.md) for claim outcomes and store contracts.
