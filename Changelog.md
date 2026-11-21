# Changelog

This file records the notable changes in each LiteBus release.

## v8.0.0-preview.6

Changes since [v8.0.0-preview.5](https://github.com/litenova/LiteBus/releases/tag/v8.0.0-preview.5). The [v8.0.0](#v800) section describes the whole 8.0.0 line. Previews of one line carry no compatibility promise between each other, so this section lists what changed without upgrade steps.

### Added

- `AuditingModuleBuilder.RequireRecord()` fails a successful mediation whose audit record cannot be written (`MessageFaultException`, code `audit.record-not-written`), so its change does not commit.
- The `LiteBus.Auditing` meter with the `litebus.audit.write.failures` counter, named on `LiteBusAuditingTelemetry`, and the `LiteBus.Auditing.Extensions.OpenTelemetry` package with `AddLiteBusAuditingMetrics()`.
- `IServiceProvider.ValidateLiteBusHost()` in `LiteBus.Runtime.Extensions.Microsoft.Hosting` validates a built host's own container without starting it; `LiteBusHostValidationException` moves to that package.

### Changed

- A failed audit write is logged at error level (event 2720) and counted, and no longer replaces the mediation's ending: a denial whose record cannot be written still reaches the caller as a denial.
- LB1021 reports only in a compilation that references the LiteBus message contracts.

### Fixed

- A denial whose audit record failed to write reached the caller as the trail's exception, and nothing logged the missing record.
- A shared `.editorconfig` with `litebus_required_declarations` raised LB1021 in projects that declare no messages.

## v8.0.0-preview.5

Changes since [v8.0.0-preview.4](https://github.com/litenova/LiteBus/releases/tag/v8.0.0-preview.4). The [v8.0.0](#v800) section describes the whole 8.0.0 line. Previews of one line carry no compatibility promise between each other, so this section lists what changed without upgrade steps.

### Added

- Every package also targets `net11.0`, compiled against the .NET 11 releases of EF Core, `Microsoft.Extensions.*`, `Microsoft.AspNetCore.OpenApi` and `System.Security.Cryptography.Pkcs`.
- `AuditChainBreak.Category` (`Integrity` or `Coverage`) and `AuditChainVerification.IsChainIntact`, which ignores coverage breaks such as a missing or stale checkpoint.
- `AuditRecordMapper<TDocument>`, a base class for a mapper that needs no services and implements only `Map`.
- `LiteBusHostValidation.Validate(IServiceCollection)` in `LiteBus.Testing.Hosting` builds a host's container with scope and build validation, resolves every manifest entry and hosted service without starting them, and reports every failure.
- Analyzer LB1030 reports a `Register<TMessage>(name, version)` call that disagrees with the type's `[MessageContract]`.
- `UseEntityFrameworkCoreStore<TContext>()` registers `IAuditDbContext` and `IAuditDbContextFactory` as default ports over the application context.

### Changed

- The audit chain stores are singletons that hold no session, context or transaction, and open their own for every operation. Each store's scoped trail stages a successful mediation's record on that mediation's business session. `IAuditChainStore<TDocument>` no longer has `StageAsync`; the stores' staging methods take the session (`MartenAuditChainStore.StageAsync(session, ...)`, `EfCoreAuditChainStore.StageAsync(businessContext, ...)`, `PostgreSqlAuditChainStore.StageAsync(..., transactionProvider, ...)`). The Entity Framework Core store takes an `IAuditDbContextFactory` instead of a context, and the PostgreSQL store no longer takes an `IPostgreSqlTransactionProvider`.
- `IAuditRecordMapper<TDocument>` has one member, `MapAsync`.
- The audit verifier asks the checkpoint policy with the segment head, so a segment that is not yet due for its first checkpoint under a time-based policy verifies intact.

### Fixed

- A host using the Marten, Entity Framework Core or PostgreSQL audit store failed to start under Development scope validation, because the audit engine singletons resolved the scoped chain store from the root provider.
- A segment that was never checkpointed reported a `CheckpointMissing` break under a daily or weekly checkpoint policy, even on the day it opened.

## v8.0.0-preview.4

Changes since [v8.0.0-preview.3](https://github.com/litenova/LiteBus/releases/tag/v8.0.0-preview.3). The [v8.0.0](#v800) section describes the whole 8.0.0 line.

### Added

- `AuditMappingContext.Features` holds the audit store's write session (`IDocumentSession`, `DbContext`, or `NpgsqlConnection` and `NpgsqlTransaction`), so a row the record mapper writes commits with the record.
- `IAuditChain.CutCheckpointAsync(segment, label)` stores a label on an explicit cut.
- `AuditHashChainBuilder.AddScheme(name, hasher, linkRule)` defines an application record hash scheme, such as a keyed HMAC, under the one conflict rule.
- `AuditHashChainBuilder.SegmentBy<TDocument>(Func<TDocument, string>)` for every audit store.
- `CheckpointPolicies.DailyInTenantZone` and `WeeklyInTenantZone` read each tenant's zone from `ITenantMetadataProvider` when the policy decides; `AuditCheckpointCandidate.Services` carries the deciding pass's scope.
- `CheckpointPublishHostOptions.Enabled`, and `AuditingModuleBuilder.ConfigureCheckpointPublishing` and `ConfigureLinkSweep`; the composition model's `auditing` section shows both loop switches, the audited axes and the record writer.
- `RefusalProblemOptions.UnmappedValidationCode` (and on `LiteBusExceptionHandlerOptions`), a separate unmapped-code policy for invalid results.
- `FieldValidationErrorsWriter` writes validation failures as `{ field, code, message }` objects with configurable member names.
- The `LiteBus.Extensions.AspNetCore.OpenApi` package adds `code`, `traceId` and `errors` to the OpenAPI problem schema, and optionally each status's refusal codes from the catalog.
- `OutboxModuleBuilder.StageDomainEvent<TEvent>()` stages an event unchanged as its own decision kind; the outbox composition section lists every domain event decision.
- `ICompositionModel.Handlers` lists every handler with its message, stage, assembly, subscriber name, delivery and priority, and `ToJson()` includes it.
- `ICommandBatchParticipant` is public in `LiteBus.Commands.Abstractions`, with `BeginAsync` and `CommandBatchStart` for items every command of a batch receives.

### Changed

- Every audit store awaits `IAuditRecordMapper.MapAsync` on the success and the out-of-band path, and the mapper is resolved from the mediation's scope.
- Expiring an expired segment returns its stored final checkpoint; an expiry retried after a partial attempt reuses the final checkpoint that attempt stored.
- `RecurringCommandHostOptions.Schedule` takes `ISchedule`, so a schedule from `CronSchedules.FromExpression` drives a single recurring loop.

### Fixed

- `IAuditRecordMapper.MapAsync` was declared and documented but never called.

### Breaking

- All audit types move out of `LiteBus.Messaging.Abstractions` and `LiteBus.Messaging`: the declarations, `[Audited]`, `[AuditExempt]`, `AuditRecord`, `IAuditTrail` and the other recording ports are in `LiteBus.Auditing.Abstractions` (namespace `LiteBus.Auditing.Abstractions`), and the record writer is in `LiteBus.Auditing`.
- `messaging.AddAuditing(...)`, the `UseAudit*` and `RequireAudit*` methods on `MessageModuleBuilder`, and `EnableAuditing()` on the command, query and event builders are removed. Configure auditing with `liteBus.Modules.AddAuditing(auditing => auditing.ForCommands()...)`; the axes, `UseTrail`, `UseActorResolver`, `UseOutcomeMapper`, `UseRecordWriter`, `RequireUniqueActions` and `RequireActionFormat` are on `AuditingModuleBuilder`. Composition fails when no axis is selected.
- `IMessageDeclarations.Audited` and `NotAudited`, `IMessageCatalog.Audited()` and `MessageCatalogEntry.Audit` become extension methods in `LiteBus.Auditing.Abstractions`; `entry.Audit` is `entry.GetAudit()`. The audit members of `LiteBusCompositionSummary` are removed.
- `IAuthorizationCondition.EvaluateAsync` returns `ValueTask<PolicyResult>`, so a condition can report a fault (HTTP 500).
- `IAuditRecordMapper` is registered scoped instead of singleton.

## v8.0.0-preview.3

Changes since [v8.0.0-preview.2](https://github.com/litenova/LiteBus/releases/tag/v8.0.0-preview.2). The [v8.0.0](#v800) section describes the whole 8.0.0 line.

### Added

- `MessageEvaluationContext` is the context every evaluation-time port receives; `AuthorizationContext` derives from it.
- Lineage: `MediationCause` on `IExecutionContext.Cause` and on the three mediation settings, `ExecutionIdentity.System`, and `RootMediationScope` for work the library starts.
- Faults: `MessageFaultException` ends a mediation as `Failed`, and `PolicyResult` (`Allow`, `Deny`, `Fault`) is what a `MessagePolicy` returns. `RefusalKind` classifies refusal codes for every transport.
- `IRefusalExceptionTranslator`, registered with `MessageModuleBuilder.UseRefusalExceptionTranslator<T>()`, turns handler exceptions into refusals inside the pipeline.
- Authorization: `RecentSignIn<TWindow>()` with an `IRecentSignInWindow` port, `Decide<TDecisionPoint>(action)` over `IAuthorizationDecisionPoint`, and `WithRefusal(code, reason, whenInnerCode)`.
- Tenancy: `UseTenancy(tenancy => tenancy.ValidateTenant(...))` with the `tenancy.invalid` code, ordered `ExecutionIdentityOptions.TenantClaimTypes`, and the `ITenantMetadataProvider` port.
- `ICompositionModel`, passed to `ValidateComposition` checks: declaration provenance per message, declared and registered policy names, contracts, ports and feature sections, with `ToJson()`.
- `RegistrationConflict` and `Replace` on the dependency registry and `MessagePolicyBuilder`.
- Domain events: `TranslateDomainEvent`, `IgnoreDomainEvent`, `PublishDomainEventInProcess`, `RequireDomainEventDecision`, an open-generic `HarvestDomainEvents` overload and the predicate form `HarvestDomainEventsWhere`. The Marten harvester reads deleted documents.
- HTTP problem details: a `code` extension, `IProblemTypeFormatter`, `IValidationErrorsWriter` with the RFC 9457 array shape, `IProblemDetailsEnricher`, `MapInvalid` and `MapDenied`, `Replace`, and a catalog export.
- Recurring commands: `RecurringCommandHostOptions.SourceIdentity`, per-tenant schedules (`ITenantSchedule` with a zone per tenant), occurrence keys, `RecurringLeaseScope`, `RecurringMissedOccurrencePolicy`, the `IRecurringOccurrenceStore` port with a PostgreSQL store, and the `LiteBus.Commands.Recurring.Schedules.Cron` package.
- `ISchedule` and the built-in schedules in `LiteBus.Runtime.Abstractions.Scheduling` and `LiteBus.Runtime.Scheduling`.
- The auditing package family: `LiteBus.Auditing.Abstractions`, `LiteBus.Auditing`, `LiteBus.Auditing.Storage.Marten`, `.PostgreSql` and `.EntityFrameworkCore`, and the checkpoint anchors `LiteBus.Auditing.Anchors.S3`, `.AzureBlob` and `.Timestamping` (RFC 3161).
- Audit capture modes (`AuditCapture.WithChange`, `AfterCommitThroughOutbox`, `BeforeAndOutcome`) and link modes (`AuditLink.AfterCommit`, `InTransaction`, `AtWrite`), with a per-message capture declaration.
- Audit checkpoints with a label, publish state, retries and several signatures; streaming and multi-source verification; segment listing, expiry and erasure; the `sha256-v2` framed scheme.
- The `litebus_forbid_nested_commands` `.editorconfig` option enables LB1023.
- `AGENTS.md` and the architecture overview state the feature design rules.

### Changed

- `WithRefusal` rewrites denials only; a policy fault passes through.
- Immediate dispatch, outbox and inbox delivery, recurring passes and tenant sources run as root mediations with a `Cause`, so `ForbidNestedCommands` and `SentBy` no longer see the triggering command as a parent.
- An identical repeated `DeclareDefault`, policy or refusal mapping is a no-op; a conflicting one fails.
- Audit success records commit with the business change by default and are linked to the chain after commit.
- Checkpoints are published by a background retrier, never inside an append, and verification compares against every registered checkpoint source.
- A LiteBus default registration yields to an explicit registration of the same service made earlier, so a store module that binds its own port keeps it.
- Each audit store package registers the canonicalizer for its own record type as a replaceable default, and host startup fails when the chain's write scheme cannot be built.

### Fixed

- A publisher that threw a concurrency exception made the checkpoint retry loop spin forever.
- The checkpoint scan swallowed failures without logging them.
- A segment that never checkpointed verified clean once its checkpoint policy was due. A segment younger than its first checkpoint still verifies clean.
- Records staged in a business transaction were never linked when the link sweep looked only at segment heads; every store now lists segments with unlinked records.
- `IRefusalProblemCatalog.TryResolve` reported every code as mapped by default.
- An invalid result's status depended on its number of failures. It now answers the invalid default (400) unless a code mapping is more specific, such as the `validation.*` mapping to 422 in `RefusalProblemCatalog.CreateDefault`.

### Breaking

- Policies return `PolicyResult` instead of `Verdict`. A missing permission lookup, condition, decision point, sign-in instant or recent sign-in window, and an unknown policy name, are faults (HTTP 500), not denials.
- The audit chain, checkpoint, segment, store and capture contracts move to `LiteBus.Auditing.Abstractions`. The Marten audit types move from `LiteBus.Commands.Storage.Marten` to `LiteBus.Auditing.Storage.Marten`, and `UseMartenAuditChain` is replaced by `AddAuditing(auditing => auditing.UseMartenStore())`.
- `ICheckpointPolicy`, `IAuditRecordMapper` and `IAuditChain` are asynchronous and gained members.
- `IRecurringSchedule` and `RecurringSchedules` forward to the shared `ISchedule` contract.
- `RefusalProblemCatalog.Map` throws on a conflicting mapping; use `Replace`. `ValidationErrorEntry.Pointer` is `JsonPointer` and still serializes as `pointer`.
- `ValidateComposition` checks receive `ICompositionModel` instead of `IMessageCatalog`; read the catalog from `Messages`.

## v8.0.0-preview.2

Changes since [v8.0.0-preview.1](https://github.com/litenova/LiteBus/releases/tag/v8.0.0-preview.1). The [v8.0.0](#v800) section describes the whole 8.0.0 line.

### Added

- `IExecutionContext.Parent` and `MessageType`, mediation nesting rules (`IMediationNestingRule`), and `CommandModuleBuilder.ForbidNestedCommands()`.
- Authorization: `MessagePolicy.Public()`, `Tenant()`, `Condition<TCondition>()`, `SentBy<TParent>()`, `WithRefusal(code, reason)`, `RecentSignIn(maxAge, claimType)`, the `ISignInInstantSource` port, and the `PermissionLookupMissing`, `ConditionMissing`, `SignInInstantUnknown` and `NotSentBy` refusal-code constants.
- `HandlerPriorities.Authorization` and `HandlerPriorities.Tenancy`.
- Pluggable audit hash chain: mapper, segmenter, canonicalizer, link rule, hasher, hash scheme, checkpoint policy, signer, publisher and source ports; `IAuditChain` with verify, cut and expire; SHA-256 and HMAC-SHA-256 hashers; `AuditChainVerifier<TDocument>`.
- `MessageCompletionContext.Identity`.
- `LiteBusExceptionHandler` and `AddLiteBusExceptionHandler()`, `IRefusalProblemCatalog.TryResolve`, and an `errors` extension on invalid results.
- `MessageModuleBuilder.DeclareConvention<TValue>()` and `DeclarationEnforcement.Report` for `RequireDeclaration`.
- `CommandMediationSettings.ForTenant`, `QueryMediationSettings.ForTenant` and `EventMediationSettings.ForTenant`.
- Recurring commands: identities, per-item batches, `AddRecurringForEachTenant` with `IRecurringTenantSource`, and `IRecurringSchedule` with `RecurringSchedules.Every`, `DailyAt` and `WeeklyOn`.
- The `LiteBus.Saga.Storage.Marten` package.
- Domain event harvesting: `IDomainEventHarvester`, `IHasDomainEvents`, `OutboxModuleBuilder.HarvestDomainEvents<TAggregate>(events, clear)`, and `HarvestDomainEvents()` on the Marten and EF Core outbox storage builders.
- Analyzer LB1029.

### Changed

- `AddAuthorizationPolicies` merges repeated calls, and an unknown policy name fails composition.
- The authorization policy guard skips events unless an event declares authorization.
- The authorization guard runs before the tenancy guard regardless of registration order.
- LB1003 allows `IQueryMediator` in query handlers; LB1008 and LB1009 check public messages in the handler assembly named by `MessageContractsRegisteredIn`; LB1023 also covers pre-handlers and post-handlers; the LB1025 message names the outbox interface for the resolved storage.

### Fixed

- `RecentSignIn` compares ages as `TimeSpan` rather than whole seconds.
- Application module discovery skips private nested module types.

### Breaking

- `AuditCheckpoint` and `ICheckpointPublisher` move to `LiteBus.Messaging.Abstractions.Auditing`, and `AuditCheckpoint` gains an id, a count, a hash scheme, a kind and a signature.
- `MartenAuditTrailOptions.UseHashChain` takes a builder; `SegmentOf`, `CheckpointPublisher`, `CheckpointInterval` and `DefaultSegment` are removed; `VerifyChainAsync` returns `AuditChainVerification`.
- `RecurringCommandBatch` holds `Items` instead of `Commands`.

## v8.0.0

This release makes identity and tenancy part of the pipeline, adds composable authorization policies, lets each assembly of a modular monolith contribute its own LiteBus configuration, and turns several silent misconfigurations into startup failures or warnings. Upgrade if you run several tenants, carry the caller on every message, write your own authorization policy engine, or split one application into module assemblies. The breaking changes are a namespace move for the audit types, the removed `RequireHandler` setter, and new composition and startup checks; the [v8 migration guide](site/content/docs/migration/v8.md) lists each upgrade step.

### Added

- Every package targets `net11.0` as well as `net10.0`. The `net11.0` build compiles against the .NET 11 release of each Microsoft dependency (EF Core, `Microsoft.Extensions.*`, `Microsoft.AspNetCore.OpenApi` and `System.Security.Cryptography.Pkcs`, all at 11.0.0), so an application on .NET 11 resolves them at 11.0.0 or later. The `net10.0` build keeps the 10.0.14 floors.
- `ExecutionIdentity` (`Principal`, `TenantId`) on `IExecutionContext.Identity`. The mediator resolves one identity per mediation: the `Identity` passed on `CommandMediationSettings`, `QueryMediationSettings` or `EventMediationSettings`, else the identity of the enclosing mediation, else the registered `IExecutionIdentityProvider`, else `ExecutionIdentity.Anonymous`. Messages no longer need a caller property. Without an `IAuditActorResolver`, the audit actor comes from the principal's `NameIdentifier` or `sub` claim, and audit records take their tenant from the identity when the trace items carry none. `AuthorizationGuard<TMessage>` exposes `CurrentIdentity`. See [Execution Context](site/content/docs/concepts/execution-context.md).
- Tenancy as a declared pipeline position in `LiteBus.Messaging.Abstractions.Tenancy`: `TenancyDeclaration.Required()`, `CrossTenant(reason)` and `None(reason)`, `ITenancyDefinition<TMessage>`, and `[TenantRequired]`, `[CrossTenant]` and `[NoTenant]`. `MessageModuleBuilder.UseTenancy()` adds `TenancyGuard<TMessage>`, which refuses a tenant-required message whose identity has no tenant with the `tenancy.required` code. `RequireDeclaration<TenancyDeclaration>()` makes every message decide. See [Tenancy](site/content/docs/concepts/tenancy.md).
- `OutboxModuleBuilder.UseAmbientTenant()` and `InboxModuleBuilder.UseAmbientTenant()` stamp the execution identity's tenant on envelopes whose item states no tenant; an explicit `TenantScope.Isolated` always wins. The in-process outbox and inbox dispatchers restore the envelope tenant into the identity, so a reaction runs in the tenant of the fact. A principal is never persisted.
- `OutboxProcessorOptions.MaxLeasedPerTenant` caps how many envelopes of one tenant a lease returns, so one busy tenant cannot fill every batch. The in-memory, PostgreSQL and EF Core outbox stores honour `OutboxLeaseRequest.MaxPerTenant`, and the shared store contract tests prove it.
- Composable authorization policies in `LiteBus.Messaging.Abstractions.Authorization`: `MessagePolicy.SignedIn()`, `Permission(name)`, `Claim(type, value)`, `RecentSignIn(maxAge)`, `System()`, `Self<TMessage>(selector)`, `All`, `Any`, `When(condition, inner)`, `Allow()` and `Deny(reason)`; the `IPermissionLookup` port; and `IAuthorizationCondition`, which runs for every authorized message before the named policy. Register them with `messaging.AddAuthorizationPolicies(policies => ...)`, which adds `PolicyAuthorizationGuard<TMessage>`. Refusal codes are on `AuthorizationPolicyRefusalCodes`. See [Authorization Policies](site/content/docs/concepts/authorization-policies.md).
- `IApplicationModule` lets each assembly contribute its handlers, contracts, outbox registrations and recurring jobs. Register modules with `AddApplicationModule<TModule>()` or `AddApplicationModulesFromAssembly(assembly)`. `AddMessaging`, `AddCommands`, `AddQueries`, `AddEvents`, `AddInbox` and `AddOutbox` may now be called more than once: a later call configures the module already registered. `IModuleRegistry.Find<TModule>()` returns a registered module. See [Application Modules](site/content/docs/architecture/application-modules.md).
- `LiteBus.Extensions.AspNetCore`: `AddLiteBusExecutionIdentity()` registers `HttpContextExecutionIdentityProvider`, which reads `HttpContext.User` and the tenant claim named by `ExecutionIdentityOptions.TenantClaimType` (default `tenant_id`). `MediationResultHttpExtensions` writes RFC 9457 problem details from `IRefusalProblemCatalog`, whose default maps `authorization.not-signed-in` to 401, other `authorization.*` codes, `declaration.missing` and `tenancy.required` to 403, `concurrency.conflict` to 409, and `validation.*` to 422.
- `LiteBus.Commands.Storage.Marten`: `MartenAuditTrailOptions.UseHashChain()` gives every audit record a sequence number within its segment (by default category and year) and a SHA-256 hash linked to its predecessor; the pluggable chain entry below covers its configuration. `MartenConcurrencyRefusalClassifier` maps Marten concurrency conflicts to the `concurrency.conflict` refusal. `UseMartenTenantSessions()` registers `MartenTenantSessionFactory`, which opens sessions for the identity's tenant; the sessions are outside the unit of work, so use them for tenant-filtered reads.
- `MessageContractsRegisteredInAttribute`: a contracts assembly marked `[assembly: MessageContractsRegisteredIn("Module.Assembly")]` hands LB1007 and LB1017 to the named assembly, which then reports any contract of the contracts assembly it does not register.
- Analyzers: LB1025 reports `IOutbox` injected into a command handler in a compilation that references EF Core, PostgreSQL or Marten outbox storage. LB1026 reports `ITransactionalOutbox<TContext>` injected for a context that no `UseDbContext<TContext>()` call in the compilation configures. LB1027 reports one `[SubscriberName]` shared by handlers of different messages. LB1028 (info) suggests splitting a definition class that declares more than one concern.
- `AuditDeclaration.Audited(action, category, targetKind, reasonRequired)`: the optional parameters let `WithTargetFrom` and `WithReasonFrom` chain without a `with` expression.
- `LiteBusCompositionSummary.RelaxedSafeguards` lists safeguards the application turned off on purpose, with the reason it gave.
- The `litebus.outbox.immediate_dispatch.skipped_scheduled` counter, tagged with `litebus.contract.name`, counts envelopes addressed to `[DispatchImmediately]` that were staged with a scheduled visibility and so never take the immediate path; a Debug log names the contract and handler.
- Documentation: a complete [ASP.NET Core, EF Core and outbox registration](site/content/docs/getting-started/aspnetcore-efcore-outbox.md) page that marks each line required or optional and states what omitting it breaks; an [options defaults](site/content/docs/operations/defaults.md) page that a unit test compares with the options types; subscriber naming and retirement guidance; one definition class per concern in every example; reading `HandledAt` from the execution context; and `ValidateComposition` as the hook for custom declaration checks.
- `IExecutionContext.MessageType` and `IExecutionContext.Parent` expose the chain of mediations that led to the current one; both have defaults so custom contexts compile unchanged. `IMediationNestingRule` lets an application refuse a nested mediation before its pipeline starts, and `CommandModuleBuilder.ForbidNestedCommands()` registers the built-in rule that throws `NestedMediationException` (code `commands.nested-command`) when a command is sent while another command is being handled, directly or through an in-process event handler. Queries nested in a command stay allowed, and outbox and inbox delivery are never nested because they start a root mediation.
- Authorization policy combinators: `MessagePolicy.Public()` for permanent public routes, `Tenant()` so a policy can order sign-in, tenant and permission checks itself, `Condition<TCondition>()` for a condition that runs only where a policy names it, `SentBy<TParent>()` to admit a call from a named enclosing use case, and `WithRefusal(code, reason)` to keep an application's own refusal codes on any combinator. `RecentSignIn` reads the sign-in instant from an `ISignInInstantSource` when one is registered, or from a claim named by `RecentSignIn(maxAge, claimType)`. `AuthorizationPolicyRefusalCodes` gains `PermissionLookupMissing`, `ConditionMissing`, `SignInInstantUnknown` and `NotSentBy`. `MessagePolicyBuilder` gains `RegisterCondition<TCondition>()` and `UseSignInInstantSource<TSource>()`, and `MessagePolicyRegistry.PolicyNames` lists the registered names.
- `HandlerPriorities.Authorization` and `HandlerPriorities.Tenancy` fix the order of the two shipped guards: authorization first, so a signed-out caller receives the authorization refusal before the tenancy one.
- The audit hash chain is built from replaceable pieces in `LiteBus.Auditing.Abstractions`: `IAuditRecordMapper<TDocument>` for the stored document, `IAuditSegmenter<TDocument>` for segments and genesis documents, `IAuditCanonicalizer<TDocument>`, `IAuditLinkRule` and `IAuditHasher` combined in a named `AuditHashScheme<TDocument>`, `ICheckpointPolicy`, `ICheckpointSigner`, `ICheckpointPublisher` and `ICheckpointSource`. Each document stores the scheme it was written with, so an application can change algorithm, key or canonical form from a date onward and older documents still verify. `IAuditChain` verifies a segment and returns every break with the head and checkpoint comparison (`AuditChainVerification`), cuts a checkpoint on demand, and expires a segment by publishing a final checkpoint before deleting it, so retention-driven deletion leaves the remaining segments verifiable. `LiteBus.Messaging` ships `Sha256AuditHasher`, `HmacSha256AuditHasher`, `PreviousHashLinkRule`, `CheckpointPolicies` and the storage-free `AuditChainVerifier<TDocument>`.
- `LiteBus.Commands.Storage.Marten`: `MartenAuditTrailOptions.UseHashChain(Action<MartenAuditHashChainBuilder>)` configures the chain with `StoreAs`, `SegmentBy`, `UseScheme`, `AcceptScheme`, `CheckpointWhen`, `SignCheckpointsWith` and `PublishCheckpointsTo`, each with a LiteBus default. Checkpoints are stored as `MartenAuditCheckpoint` documents and carry a record count, and a time-based checkpoint policy is also evaluated by a background service every `CheckpointScanInterval`. Existing chains verify unchanged under the default scheme `sha256-v1`.
- `MessageCompletionContext.Identity` carries the mediation's resolved identity, so an `IAuditActorResolver` reads the caller from its argument instead of ambient state.
- `LiteBus.Extensions.AspNetCore`: `LiteBusExceptionHandler`, registered with `AddLiteBusExceptionHandler()`, maps `LiteBusMessageDeniedException` and `LiteBusMessageInvalidException` through `IRefusalProblemCatalog` to the same problem details `ToHttpResult` writes; `LiteBusExceptionHandlerOptions.UnmappedRefusalCode` can report an unmapped code as a logged 500. `IRefusalProblemCatalog.TryResolve` tells a mapped code from the fallback. Invalid results carry an `errors` extension keyed by member, in the shape of `ValidationProblemDetails.Errors`.
- `MessageModuleBuilder.DeclareConvention<TValue>(convention)` derives a declaration from the message type for every message with no definition, attribute or default, such as an idempotency key read from an interface or an audit action derived from the type name. `RequireDeclaration` takes `DeclarationEnforcement.Report`, which lists undeclared messages through the `litebus.declarations` diagnostic check instead of failing composition, so declarations can be rolled out message by message.
- `CommandMediationSettings.ForTenant(tenantId)`, `QueryMediationSettings.ForTenant(tenantId)` and `EventMediationSettings.ForTenant(tenantId)`.
- Recurring commands carry an identity: `AddRecurring` takes an identity factory, and `RecurringCommandBatch` holds `RecurringCommandItem` values with an optional identity. `RecurringBatchIsolation.PerItem` sends each item on its own with its own identity and keeps going past a failing item; the default `Atomic` keeps the all-or-nothing batch. `AddRecurringForEachTenant<TCommand>` sends one command per tenant from an `IRecurringTenantSource`, isolating failures per tenant. `RecurringCommandHostOptions.Schedule` takes an `IRecurringSchedule`; `RecurringSchedules.Every`, `DailyAt` and `WeeklyOn` are built in, and a cron library plugs in through the port.
- `LiteBus.Saga.Storage.Marten`: a Marten saga store registered with `UseMarten()`, with optimistic concurrency and tenant scoping matching the PostgreSQL store.
- Domain event harvesting: `IDomainEventHarvester` moves events from tracked entities into `IDomainEventCollector` before the outbox stages them. `HarvestDomainEvents()` on the Marten and EF Core outbox storage builders registers the harvester for the document session or `DbContext`; entities implementing `IHasDomainEvents` are read by default, and `OutboxModuleBuilder.HarvestDomainEvents<TAggregate>(events, clear)` reads an aggregate base type of the application's own shape.
- Analyzer LB1029 reports a durable message type without `[MessageContract]` in an assembly that declares `MessageContractsRegisteredIn`, which would otherwise never be checked anywhere.
- Library-wide design rules, applied to every v8 feature and written into `AGENTS.md`:
  - Each built-in behavior is a port with a registered default, resolved per evaluation with `MessageEvaluationContext`.
  - One registration-conflict rule applies everywhere: an identical repeat is a no-op, a conflicting one fails naming both sources, and `Replace` overrides.
  - A configuration fault (`MessageFaultException`) is never a user refusal.
  - Work the library starts is a root mediation with a declared identity and a `MediationCause`.
  - Features that persist state are store-neutral.
  - `ICompositionModel` explains the composition, and `ToJson()` exports it.
- Authorization: `PolicyResult` with `Allow`, `Deny` and `Fault`; `RecentSignIn<TWindow>()` reads the window per evaluation; `Decide<TDecisionPoint>(action)` plugs an external decision point such as a relationship-based engine; `WithRefusal` gains an inner-code overload. Tenancy gains `ValidateTenant` with the `tenancy.invalid` code, ordered tenant claim types and the `ITenantMetadataProvider` port.
- `IRefusalExceptionTranslator` turns handler exceptions into refusals inside the pipeline, so every transport sees one refusal. The ASP.NET Core adapter adds:
  - a `code` extension;
  - a problem type formatter (URN, documentation URL or `about:blank`);
  - the RFC 9457 errors array as an option;
  - outcome-kind defaults (`MapInvalid`, `MapDenied`);
  - `Replace`;
  - a catalog export.
- Domain events gain a decide-and-translate step between harvest and stage:
  - `TranslateDomainEvent`;
  - `IgnoreDomainEvent`;
  - `PublishDomainEventInProcess`, which publishes after commit as a root mediation;
  - `RequireDomainEventDecision`.

  Harvesting accepts open generic aggregate bases and a predicate, and the Marten harvester reads deleted documents.
- Recurring commands:
  - `SourceIdentity`, which defaults to `ExecutionIdentity.System`;
  - a composition check for tenant-required commands without an identity;
  - per-tenant schedules with a zone per tenant;
  - occurrence keys, lease scopes and a missed-occurrence policy;
  - a PostgreSQL occurrence store and a Cronos-based cron package.

  The schedule contract is `ISchedule` in `LiteBus.Runtime.Abstractions`.
- The auditing package family. `LiteBus.Auditing.Abstractions` and `LiteBus.Auditing` hold a store-neutral engine:
  - capture modes `WithChange`, `AfterCommitThroughOutbox` and `BeforeAndOutcome`, and link modes `AfterCommit`, `InTransaction` and `AtWrite`;
  - checkpoints with a label, publish state and several signatures, published by a retrier;
  - streaming verification from a checkpoint and against several sources;
  - segment listing, expiry and erasure;
  - the `sha256-v2` framed scheme with a composition self-test.

  Stores ship for Marten, PostgreSQL and EF Core, proven by shared contract tests. Checkpoint anchors ship for S3 Object Lock, Azure immutable blobs and RFC 3161 time-stamps.
- Audit record mappers are resolved from the mediation's scope and receive the store's write session in `AuditMappingContext.Features`; `AddScheme` defines application hash schemes such as a keyed HMAC; `SegmentBy<TDocument>(Func<TDocument, string>)` works for every store; explicit cuts take a label; expiry is safe to retry; `DailyInTenantZone` and `WeeklyInTenantZone` read each tenant's zone from `ITenantMetadataProvider`; `ConfigureCheckpointPublishing` can switch the checkpoint loop off per host.
- HTTP problem details: a separate unmapped-code policy for validation codes (`UnmappedValidationCode`), `FieldValidationErrorsWriter` for `{ field, code, message }` errors, and the `LiteBus.Extensions.AspNetCore.OpenApi` package that adds the problem members and refusal codes to the OpenAPI document.
- `StageDomainEvent<TEvent>()` stages a domain event unchanged as its own decision; `ICompositionModel.Handlers` lists every handler with its stage, assembly, subscriber name and delivery; `ICommandBatchParticipant` is a public batch hook.
- `RecurringCommandHostOptions.Schedule` takes any `ISchedule`, including cron schedules.
- Each audit store splits its work by lifetime: a singleton chain store that opens its own session, context or connection per operation, shared by the linker, checkpoint lifecycle, verifier and background loops, and a scoped trail that stages a successful mediation's record on its business session. Hosts with Development scope validation start with any store. `UseEntityFrameworkCoreStore<TContext>()` registers `IAuditDbContext` and `IAuditDbContextFactory` as default ports.
- `IAuditRecordMapper<TDocument>` has one member, `MapAsync`; `AuditRecordMapper<TDocument>` is the base class for a mapper that needs no services.
- Audit verification reports each break's category (`Integrity` or `Coverage`) and `IsChainIntact`, so a tamper alarm ignores a checkpoint that is not cut yet. The verifier asks the checkpoint policy with the segment head, so a new segment under a time-based policy verifies intact until its first checkpoint is due.
- `LiteBusHostValidation.Validate(IServiceCollection)` in `LiteBus.Testing.Hosting` validates a host's container in a unit test: scope and build validation, every manifest entry and hosted service resolved without starting, every failure reported.
- Analyzer LB1030 reports a `Register<TMessage>(name, version)` call that disagrees with the type's `[MessageContract]`.
- A failed audit write is logged at error level and counted on the `litebus.audit.write.failures` counter of the `LiteBus.Auditing` meter (`LiteBus.Auditing.Extensions.OpenTelemetry` registers it), and the mediation's ending stands. `RequireRecord()` makes a successful mediation fail instead when its record cannot be written. Application code writes its own records through the scoped `IAuditTrail`.
- `IServiceProvider.ValidateLiteBusHost()` validates a built host's container without starting it, for a startup check such as `--validate-composition`; `LiteBusHostValidation.Validate(IServiceCollection)` in `LiteBus.Testing.Hosting` does the same in a unit test.

### Changed

- `docs/CONTEXT.md` is rewritten against the 8.0.0 source. It no longer claims that `VersionPrefix` sets the release version, which the tag does.
- CI runs the whitespace check and the NuGet vulnerability audit in their own job, packs projects in parallel, builds only the targeted project in a test shard, and splits the storage integration tests into five shards.
- The recurring command XML remarks keep the contract and failure modes, and the recurring commands page keeps tuning, operations, SQL and event ids; each links to the other.
- `AddAuthorizationPolicies` merges repeated calls into one policy set, so each module assembly can contribute its own policies; a duplicate policy name still fails. A message whose declaration names an unregistered policy now fails composition instead of denying at runtime.
- The event module declares a default authorization exemption for `IEvent`, so a composition with authorization policies publishes undeclared events, including on outbox replay. An explicit per-event declaration still wins.
- LB1003 no longer reports `IQueryMediator` in a query handler: a nested query is side-effect free. Commands, events, inbox and outbox stay reported.
- LB1008 and LB1009 skip a contracts assembly marked `MessageContractsRegisteredIn` and check its messages in the named handler assembly instead.
- LB1023 also covers command pre-handlers and post-handlers, and its description names `ForbidNestedCommands()` as the runtime counterpart. The LB1025 message names `ITransactionalOutbox<TContext>` for EF Core storage and `ITransactionalOutbox` for Marten or PostgreSQL storage.
- `RecentSignIn` compares the sign-in age as a `TimeSpan`, so windows below a second behave as written.
- A LiteBus default registration yields to an explicit registration of the same service made earlier; a conflicting explicit registration still fails composition unless it calls `Replace`.

### Breaking

- The audit types (`AuditActor`, `AuditDeclaration`, `AuditedDeclaration`, `AuditExemptDeclaration`, `AuditOutcome`, `AuditRecord`, `AuditedAttribute`, `AuditExemptAttribute`, `AuditReasonMissingException`, `IAuditActorResolver`, `IAuditDefinition<TMessage>`, `IAuditOutcomeMapper`, `IAuditRecordWriter`, `IAuditScope`, `IAuditTrail`) moved from `LiteBus.Messaging.Abstractions` to the `LiteBus.Auditing.Abstractions` package and namespace, and the record writer moved from `LiteBus.Messaging` to `LiteBus.Auditing`. Message assemblies that declare audit positions reference `LiteBus.Auditing.Abstractions` and add `using LiteBus.Auditing.Abstractions;`.
- `InProcessOutboxDispatchOptionsBuilder.RequireHandler` has no setter. Replace `dispatch.RequireHandler = false` with `dispatch.AllowEnvelopesWithoutHandler("reason")`; the reason is logged as a warning at startup and listed in the composition summary.
- Composition fails when immediate dispatch is enabled without per-handler delivery and a handler class carries `[DispatchImmediately]`, which was silently ignored. Call `EnablePerHandlerDelivery()` or move the attribute to the event type.
- The EF Core outbox and inbox storage modules add a startup task that fails startup when the configured `DbContext` cannot be resolved from a scope, naming the missing `AddDbContextFactory<TContext>` registration. Resolving `ITransactionalOutbox<TContext>` for a context no `UseDbContext<TContext>()` configured throws `LiteBusConfigurationException` instead of failing on the first enqueue.
- `AuditDeclaration.Audited` has three optional parameters. Source compiles unchanged; assemblies compiled against 7.x must be recompiled.
- `MartenAuditTrail` takes `IDocumentStore` and optional `MartenAuditTrailOptions` and `TimeProvider`. Container-built trails need no change; code that constructs it by hand passes the store. `MartenAuditRecord` gains `Segment`, `Sequence`, `PreviousHash`, `Hash` and `HashScheme`, which stay empty unless the hash chain is on.
- Calling `AddCommands`, `AddEvents`, `AddOutbox` or another module registration twice no longer throws: the second call configures the registered module.
- In-process dispatch of a tenant-scoped envelope now runs the handler with that tenant in `IExecutionContext.Identity`, and the idempotency key resolver prefixes the stored key with the identity's tenant. An application that registers an `IExecutionIdentityProvider` returning tenants where it previously set no tenant on its commands stores tenant-prefixed keys from then on, so a retry that spans the upgrade is not recognised as a repeat.
- New analyzer warnings LB1025, LB1026 and LB1027 can fail builds that treat warnings as errors.
- `AuditCheckpoint` and `ICheckpointPublisher` move from `LiteBus.Commands.Storage.Marten` to `LiteBus.Auditing.Abstractions`, and `AuditCheckpoint` gains `Id`, `Count`, `HashScheme`, `Kind` and `Signature`. `MartenAuditTrailOptions.UseHashChain` takes an `Action<MartenAuditHashChainBuilder>`; `SegmentOf`, `CheckpointPublisher`, `CheckpointInterval` and `DefaultSegment` are removed in favour of `SegmentBy`, `PublishCheckpointsTo` and `CheckpointWhen`. `MartenAuditTrail.VerifyChainAsync` returns `AuditChainVerification` instead of the first broken sequence. These replace the 8.0.0-preview.1 API.
- `RecurringCommandBatch` holds `Items` (`RecurringCommandItem`) instead of `Commands`. `RecurringCommandBatch.From(IReadOnlyList<ICommand>)` still builds a batch from plain commands.
- Application module discovery skips private nested module types, as documented. A private nested `IApplicationModule` that was picked up by accident is no longer registered.
- `MessagePolicy.EvaluateAsync` returns `PolicyResult` instead of `Verdict`, and `WithRefusal` rewrites denials only. A missing permission lookup, condition, decision point, sign-in instant or recent sign-in window, or an unknown policy name, now raises `MessageFaultException` (HTTP 500) instead of a denial. `IAuthorizationCondition.EvaluateAsync` returns `PolicyResult` as well, so a condition can report a fault.
- The audit chain, checkpoint, segment, store and capture contracts move to `LiteBus.Auditing.Abstractions`, with the declarations, records and trail. The Marten audit trail and chain move to `LiteBus.Auditing.Storage.Marten`, and `UseMartenAuditChain` with `MartenAuditTrailOptions` is replaced by `AddAuditing(auditing => auditing.UseMartenStore().UseHashChain(...))`. Success records commit with the business change by default.
- `ICheckpointPolicy.IsDueAsync`, `IAuditRecordMapper.MapAsync` and the new `IAuditChain` members replace their synchronous or narrower predecessors.
- `RefusalProblemCatalog.Map` throws on a conflicting mapping (`Replace` overrides), `IRefusalProblemCatalog.TryResolve` returns `false` by default, and invalid results map by outcome, defaulting to 400.
- `ValidateComposition` checks receive `ICompositionModel`; the message catalog is its `Messages` property.
- Auditing is configured on one module: `liteBus.Modules.AddAuditing(auditing => auditing.ForCommands()...)` selects the audited axes and holds the trail, actor resolver, outcome mapper, record writer and action checks. `messaging.AddAuditing`, the `UseAudit*` and `RequireAudit*` methods on `MessageModuleBuilder`, and `EnableAuditing()` on the axis builders are removed, and composition fails when no axis is selected. `IMessageDeclarations.Audited`, `IMessageCatalog.Audited()` and `MessageCatalogEntry.Audit` are extension methods in `LiteBus.Auditing.Abstractions` (`entry.GetAudit()`).
- `IAuditRecordMapper` is resolved from the mediation's scope, and every audit store awaits `MapAsync` with the store's write session in `AuditMappingContext.Features`.

## Earlier Releases

Release notes for v7.2.0 and earlier are in [Changelog.Archive.md](Changelog.Archive.md).
