# Handler Resolution Internals

This page explains how the message registry turns registered CLR types into the handler lists a strategy runs, and why direct, indirect, and open generic handlers resolve the way they do. Read it after [The Handler Pipeline](../concepts/handler-pipeline.md) when you need to reason about resolution edge cases or extend the registry. The behavior here is grounded in `MessageRegistry` and `MessageDescriptor` under `src/LiteBus.Messaging/Registry`.

## The Two Views

The registry keeps two complementary views of the same data:

- A message-centric view: one `MessageDescriptor` per message type, each holding the handler descriptors that apply to it.
- A handler-centric view: `Handlers`, an ordered list of every handler descriptor in registration order. Modules read `Count` as an index to register only the descriptors added since their last pass, which keeps DI registration incremental.

Both views are built during registration, not at mediation time. Mediation reads the finished descriptor and resolves handler instances from the container.

## Registration Pass

Each call to `Register(Type)` runs under a lock and does this:

1. Skip the type if it was processed before. `_processedTypes` prevents duplicate work when the same assembly is scanned twice.
2. Run the four descriptor builders against the type: `HandlerDescriptorBuilder`, `ErrorHandlerDescriptorBuilder`, `PostHandlerDescriptorBuilder`, `PreHandlerDescriptorBuilder`. Each builder reports whether it can build from the type and, if so, produces handler descriptors.
3. Branch on the result:
   - No descriptors: treat the type as a message type and register it.
   - Descriptors whose message type is a bare generic parameter, on an open generic type definition: store it as an open generic handler for later closing.
   - Otherwise: process the handler descriptors and link them to known messages.
4. Link handlers to pending messages, mark the type processed, then commit pending messages into the committed list.

The pending-then-commit split lets a single registration pass discover a message and its handlers in any order and still link them before the descriptor becomes visible.

## Direct Versus Indirect Handlers

When a handler descriptor is added to a message descriptor, `MessageDescriptor.AddDescriptor` routes it by comparing types:

| Relationship | Bucket | Example |
| --- | --- | --- |
| Handler's message type equals the message type | Direct (`Handlers`, `PreHandlers`, `PostHandlers`, `ErrorHandlers`) | `ICommandHandler<CreateProductCommand>` for `CreateProductCommand` |
| Message type is assignable to the handler's message type | Indirect (`IndirectHandlers`, `IndirectPreHandlers`, and so on) | A pre-handler for `ICommand` applied to every command; a handler for a base event applied to a derived event |

Indirect handlers are how global and polymorphic handlers work. A pre-handler registered against the `ICommand` marker is a direct handler of `ICommand` and an indirect handler of every concrete command, because each command is assignable to `ICommand`. The same mechanism drives [Polymorphic Dispatch](../concepts/polymorphic-dispatch.md): a handler for a base type is picked up as an indirect handler of its subtypes.

At mediation time `MessageDependencies` resolves both direct and indirect descriptors from the container, so a strategy sees the full set. Ordering across direct and indirect is what [Handler Priority](../concepts/handler-priority.md) describes: global (indirect) pre-handlers run before specific (direct) ones, and specific post-handlers run before global ones.

## Open Generic Handlers

An open generic handler such as `LoggingPreHandler<T> : ICommandPreHandler<T>` cannot be resolved until LiteBus knows the concrete `T`. The registry stores these separately and closes them on demand:

1. `Register` detects a generic type definition whose handler descriptor has a bare type parameter and calls `StoreOpenGenericHandler`.
2. `ThrowIfOpenGenericHandlerShapeIsUnsupported` rejects any open generic handler that does not declare exactly one type parameter, throwing `UnsupportedOpenGenericHandlerException`. This is why two-parameter shapes like `Handler<TCommand, TResult>` fail at startup. See [Open Generic Handlers](../concepts/open-generic-handlers.md).
3. The handler is closed against every already-known message type, and against each new message type as it registers. Closing runs `SatisfiesGenericConstraints` first, so a handler with `where T : ICommand` is closed only for command types.
4. `MakeGenericType` builds the closed handler type, the builders produce descriptors for it, and those descriptors join `Handlers` and the relevant message descriptor.

Because closing happens during registration, the per-message handler lists are complete before any message is mediated. There is no per-call reflection to close generics on the hot path.

## Why a Stale Registry Breaks Tests

Each LiteBus module build obtains one `IMessageRegistry` from `IModuleConfiguration.GetOrCreateContext` (created by `MessageModule`, consumed by command, query, and event modules). The same instance is registered as `IMessageRegistry`, `IMessageReader`, and `IMessageWriter` in DI for that host. Exact-type dispatch uses O(1) `IMessageReader.Find`; assignability fallback enumerates committed descriptors. For test isolation, create a new registry per test rather than reusing one instance. See [Testing LiteBus](../testing/application-testing.md).

## Next

See the vocabulary these mechanisms use in the [Glossary](../reference/glossary.md).
