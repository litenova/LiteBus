# Handler Resolution Internals

The message registry turns registered CLR types into the handler lists that a strategy runs. This page explains how direct, indirect, and open generic handlers resolve, and it describes `MessageRegistry` and `MessageDescriptor` under `src/LiteBus.Messaging/Registry`. Read [The Handler Pipeline](../concepts/handler-pipeline.md) first, then come back here when you need to reason about resolution edge cases or extend the registry.

## The Two Views

The registry keeps two complementary views of the same data:

- A message-centric view: one `MessageDescriptor` per message type, each holding the handler descriptors that apply to it.
- A handler-centric view: `Handlers`, an ordered list of every handler descriptor in registration order. Modules read `Count` as an index. They register only the descriptors added since their last pass, which keeps DI registration incremental.

Both views are built during registration, not at mediation time. Mediation reads the finished descriptor and resolves handler instances from the container.

## Registration Pass

Each call to `Register(Type)` runs under a lock and does this:

1. If the type was processed before, skip it. `_processedTypes` prevents duplicate work when the same assembly is scanned twice.
2. Run the six descriptor builders against the type: `HandlerDescriptorBuilder`, `CompletionHandlerDescriptorBuilder`, `ErrorHandlerDescriptorBuilder`, `PostHandlerDescriptorBuilder`, `PreStageHandlerDescriptorBuilder`, `RefusalMapperDescriptorBuilder`. Each builder reports whether it can build from the type and, if so, produces handler descriptors.
3. Take one of three paths depending on the result:
   - When there are no descriptors, treat the type as a message type and register it.
   - When the descriptors name a bare generic parameter as the message type on an open generic type definition, store the type as an open generic handler for later closing.
   - Otherwise, process the handler descriptors and link them to known messages.
4. Link handlers to pending messages and mark the type processed. Then commit pending messages into the committed list.

The pending-then-commit split lets a single registration pass discover a message and its handlers in any order. It still links them before the descriptor becomes visible.

## Direct Versus Indirect Handlers

When a handler descriptor is added to a message descriptor, `MessageDescriptor.AddDescriptor` routes it by comparing types:

| Relationship | Bucket | Example |
| --- | --- | --- |
| Handler's message type equals the message type | Direct (`Handlers`, `PreStageHandlers`, `PostHandlers`, `ErrorHandlers`) | `ICommandHandler<CreateProductCommand>` for `CreateProductCommand` |
| Message type is assignable to the handler's message type | Indirect (`IndirectHandlers`, `IndirectPreStageHandlers`, and so on) | A guard for `ICommand` applied to every command, and a handler for a base event applied to a derived event |

Indirect handlers implement global and polymorphic handlers. A pre-handler registered against the `ICommand` marker is a direct handler of `ICommand` and an indirect handler of every concrete command, because each command is assignable to `ICommand`. The same mechanism drives [Polymorphic Dispatch](../concepts/polymorphic-dispatch.md). A handler for a base type is picked up as an indirect handler of its subtypes.

`MessageDependencies` resolves both direct and indirect descriptors from the container at mediation time, so a strategy sees the full set. [Handler Priority](../concepts/handler-priority.md) describes the ordering across direct and indirect. Global (indirect) pre-handlers run before specific (direct) ones, and specific post-handlers run before global ones.

## Open Generic Handlers

LiteBus cannot resolve an open generic handler such as `LoggingPreHandler<T> : ICommandPreHandler<T>` until it knows the concrete `T`. The registry stores these separately and closes them on demand:

1. `Register` detects a generic type definition whose handler descriptor has a bare type parameter and calls `StoreOpenGenericHandler`.
2. `ThrowIfOpenGenericHandlerShapeIsUnsupported` validates the handler shape, throwing `UnsupportedOpenGenericHandlerException` for anything it cannot close. One parameter binds the message type. When the handler implements a typed contract over both parameters in order (for example `ICommandPostHandler<TCommand, TCommandResult>`), two parameters are closed. A second parameter that is the handler's own invention, such as `TContext` or `TStore`, has nothing to bind to and is still rejected. See [Open Generic Handlers](../concepts/open-generic-handlers.md).
3. The handler is closed against every already-known message type, and against each new message type as it registers. Closing builds type arguments with `BuildOpenGenericTypeArguments`, checks `CanCloseForMessage`, and lets the CLR enforce generic constraints through `MakeGenericType`. A constraint mismatch skips the message, so a handler with `where T : ICommand` is closed only for command types.
4. `MakeGenericType` builds the closed handler type. The builders produce descriptors for it. Those descriptors join `Handlers` and the relevant message descriptor.

Because closing happens during registration, the per-message handler lists are complete before any message is mediated. There is no per-call reflection to close generics on the hot path.

## Registry Isolation in Tests

Each LiteBus module build obtains one `IMessageRegistry` from `IModuleConfiguration.GetOrCreateContext` (created by `MessageModule`, consumed by command, query, and event modules). The same instance is registered as `IMessageRegistry`, `IMessageReader`, and `IMessageWriter` in DI for that host. Exact-type dispatch uses O(1) `IMessageReader.Find`. Assignability fallback enumerates committed descriptors. For test isolation, create a new registry per test rather than reusing one instance. See [Testing LiteBus](../testing/application-testing.md).

## Next

See the vocabulary these mechanisms use in the [Glossary](../reference/glossary.md).
