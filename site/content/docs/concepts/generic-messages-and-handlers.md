# Generic Messages and Handlers

A generic message is a message with type parameters, such as `CreateEntityCommand<TEntity, TKey>`, handled by a matching generic handler. You write one command and one handler for an operation that applies to many entity types. That single pair covers CRUD and generic repository patterns without a copy per entity.

This page assumes you have read the [Command Module](commands.md). For the related pattern where the handler is generic but the message is not, see [Open Generic Handlers](open-generic-handlers.md).

## Generic Messages with Generic Handlers

Instead of creating separate commands like `CreateProductCommand` and `CreateUserCommand`, create a single `CreateEntityCommand<TEntity>` that covers every entity type.

## How to Implement Generic Messages

### 1. Define a Generic Message

Create a command, query, or event with one or more generic type parameters.

```csharp
/// <summary>
/// A generic command for creating any entity, returning the entity's ID.
/// </summary>
public sealed class CreateEntityCommand<TEntity, TKey> : ICommand<TKey>
    where TEntity : IEntity<TKey>
{
    public required TEntity EntityData { get; init; }
}
```

### 2. Implement a Generic Handler

The handler must also be generic, and every one of its type parameters must appear in the message type it handles. [How LiteBus Closes a Generic Handler](#how-litebus-closes-a-generic-handler) gives the full rule.

```csharp
/// <summary>
/// A generic handler for creating any entity.
/// </summary>
public sealed class CreateEntityCommandHandler<TEntity, TKey>
    : ICommandHandler<CreateEntityCommand<TEntity, TKey>, TKey>
    where TEntity : class, IEntity<TKey>
{
    private readonly IRepository<TEntity, TKey> _repository;

    public CreateEntityCommandHandler(IRepository<TEntity, TKey> repository)
    {
        _repository = repository;
    }

    public async Task<TKey> HandleAsync(CreateEntityCommand<TEntity, TKey> command, CancellationToken cancellationToken = default)
    {
        await _repository.SaveAsync(command.EntityData, cancellationToken);
        return command.EntityData.Id;
    }
}
```

### 3. Register the Generic Handler

Register a generic handler with the dependency injection container by its open generic type definition.

```csharp
// In Program.cs
builder.Services.AddLiteBus(builder =>
{
    builder.AddMessaging(_ => { });
    builder.AddCommands(module =>
    {
        // Register the open generic type.
        module.Register(typeof(CreateEntityCommandHandler<,>));
    });
});
```

### 4. Use the Generic Message

When you send a generic message, you provide the concrete types. LiteBus and the DI container resolve and instantiate the matching closed generic handler (e.g., `CreateEntityCommandHandler<Product, Guid>`).

```csharp
// In a service or controller

// Create a new Product
var newProduct = new Product { Name = "Laptop", Price = 1200 };
var productId = await _commandMediator.SendAsync(new CreateEntityCommand<Product, Guid>
{
    EntityData = newProduct
});

// Create a new User
var newUser = new User { Username = "testuser" };
var userId = await _commandMediator.SendAsync(new CreateEntityCommand<User, int>
{
    EntityData = newUser
});
```

## How LiteBus Closes a Generic Handler

LiteBus closes a generic handler for each message you send by matching the message type the handler names against the runtime message. For `CreateEntityCommandHandler<TEntity, TKey>`, the handler names `CreateEntityCommand<TEntity, TKey>`, so sending a `CreateEntityCommand<Product, Guid>` binds `TEntity` to `Product` and `TKey` to `Guid`. LiteBus closes the handler type and every contract it implements from those bindings.

The match is structural, so a type parameter can sit anywhere in the message type:

| Handler | Message sent | Closed handler |
| --- | --- | --- |
| `ListQueryHandler<T> : IQueryHandler<ListQuery<T>, List<T>>` | `ListQuery<string>` | `ListQueryHandler<string>` |
| `RenameHandler<TKey, TEntity> : ICommandHandler<RenameCommand<TEntity, TKey>, string>` | `RenameCommand<Product, Guid>` | `RenameHandler<Guid, Product>` |
| `ListEnvelopeHandler<T> : IQueryHandler<EnvelopeQuery<List<T>>, string>` | `EnvelopeQuery<List<int>>` | `ListEnvelopeHandler<int>` |
| `EntityChangedHandler<TEntity> : IEventHandler<IEntityChanged<TEntity>>` | `CustomerChanged : IEntityChanged<Customer>` | `EntityChangedHandler<Customer>` |

The last row shows that the match also looks at the message's base types and interfaces, so a generic handler for a generic interface reaches a non-generic message that implements it. The same rule closes pre-handlers, post-handlers, error handlers, and completion handlers.

A handler applies to a message only when the match succeeds and the handler's constraints accept the bound types. The pipeline leaves out every other handler for that message, so two handlers of one generic message can divide its closed types between them. For example, a handler constrained `where T : class` and another constrained `where T : struct` each handle their own half of `DescribeQuery<T>`.

A handler registered for one closed message, such as `IQueryHandler<GetByIdQuery<Order>, Order>`, overrides the generic handler for that message only. Every other closed type still reaches the generic handler.

Open generic pipeline handlers that apply to every message, such as `CommandLoggingPreHandler<T>` or an authorization guard over `T`, reach generic messages too. A closed generic message is registered on its first dispatch, and the registry closes every open generic handler over it then, as [Open Generic Handlers](open-generic-handlers.md) describes.

The mediator throws `PipelineContractException` when a handler cannot be closed at all:

- A type parameter of the handler does not appear in the message type it handles, so no message can bind it.
- The message implements the handled generic interface more than once with different type arguments, such as both `IEntityChanged<Customer>` and `IEntityChanged<Product>`. Register a handler for the message itself instead.

## Use Cases for Generic Messages

1.  **Generic CRUD Operations**: A standard set of generic commands and queries (`CreateEntityCommand<T>`, `GetEntityByIdQuery<T>`, `UpdateEntityCommand<T>`, `DeleteEntityCommand<T>`) covers every entity.
2.  **Logging and Auditing**: A generic `LogActivityCommand<TPayload>` logs user activities with strongly-typed payloads.
3.  **Data Synchronization**: A generic `SyncDataCommand<TSource, TDestination>` synchronizes data between systems for any source and destination types.

## Open Generic Handlers

A related pattern keeps the message fixed and makes the handler generic over the message type, so one cross-cutting behavior applies to many messages. See [Open Generic Handlers](open-generic-handlers.md) for that pattern. The two patterns combine. A `CreateEntityCommand<T>` handled by a `CreateEntityCommandHandler<T>` can still be logged by a `CommandLoggingPreHandler<T>` that applies to all commands.

## Durable Generic Messages

Generic commands and events work with the durable inbox and outbox, but each durable contract must be closed and stable. A stored row needs one contract name, one version, and one concrete CLR type for deserialization.

```csharp
public sealed record ArchiveCommand<TPayload>(TPayload Payload) : ICommand;
public sealed record ExportCompletedEvent<TPayload>(Guid ExportId);

builder.Services.AddLiteBus(builder =>
{
    builder.AddMessaging(_ => { });
    builder.AddInbox(inbox =>
    {
        inbox.Contracts.Register<ArchiveCommand<CustomerSnapshot>>(
            "archive.commands.customer-snapshot",
            version: 1);
    });

    builder.AddOutbox(outbox =>
    {
        outbox.Contracts.Register<ExportCompletedEvent<CustomerExport>>(
            "exports.events.customer-export-completed",
            version: 1);
    });
});
```

Do not register an open durable contract such as `ArchiveCommand<>`. Open generic messages do not map to a concrete deserialization type, so the registry rejects them at startup.

Use a distinct name for each closed durable message. Treat each name and version pair as persisted data, not as a reflection alias.

## Best Practices

1.  **Register Open Generic Definitions**: Register a generic handler by its open generic type, `module.Register(typeof(CreateEntityCommandHandler<,>))`, or let `RegisterFromAssembly` discover it. LiteBus builds the closed handler for each concrete message you send.
2.  **Use Constraints**: Apply generic constraints (`where T : ...`) to messages and handlers for type safety and better IntelliSense.
3.  **Close Durable Contracts**: Register a distinct, closed contract name and version for each durable generic message. Never register an open contract such as `ArchiveCommand<>`.
4.  **Combine with Open Generic Handlers**: A `CreateEntityCommand<T>` handled by `CreateEntityCommandHandler<T>` can still be logged by a `CommandLoggingPreHandler<T>` that applies to all commands. See [Open Generic Handlers](open-generic-handlers.md).
5.  **Combine with Generic Repositories**: This pattern composes with a generic repository (`IRepository<TEntity>`), as the example shows.

## Next

- [Open Generic Handlers](open-generic-handlers.md) for one handler that applies across many messages
- [Command Module](commands.md) for the pipeline these messages run through
