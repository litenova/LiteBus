# Durable Messaging Walkthrough

This walkthrough takes one command from registration to a processed inbox row: compose the host with in-memory storage, accept a command, run a processor pass, and observe the stored row through the manager API. It then shows the two swaps that production needs: PostgreSQL storage and AMQP dispatch.

> **This page is for** developers adopting durable messaging who want one working program before reading the references.
>
> **After reading it** you can run the inbox end to end and name what changes for production. For the durable overview, start with [Reliable Messaging](README.md).

## Prerequisites

A console host with the LiteBus command, inbox, in-memory storage, and in-process dispatch packages referenced, plus a `ProcessPaymentCommand` and its handler:

```csharp
public sealed record ProcessPaymentCommand(Guid PaymentId, decimal Amount) : ICommand;

public sealed class ProcessPaymentCommandHandler : ICommandHandler<ProcessPaymentCommand>
{
    public Task HandleAsync(ProcessPaymentCommand command, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"Processed payment {command.PaymentId}.");
        return Task.CompletedTask;
    }
}
```

## Register the Inbox

```csharp
var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddLiteBus(liteBus =>
{
    liteBus.AddCommands(commands =>
        commands.RegisterFromAssembly(typeof(ProcessPaymentCommand).Assembly));

    liteBus.AddInbox(inbox =>
    {
        inbox.Contracts.Register<ProcessPaymentCommand>(
            name: "payments.commands.process-payment",
            version: 1);

        inbox.UseInMemoryStorage();
        inbox.UseInProcessDispatch();
        inbox.EnableInboxProcessor();
    });
});

var host = builder.Build();
```

Four registrations make the durable path: contracts for every stored message type, one storage adapter, one dispatcher, and the processor loop. In-memory storage keeps rows process-wide; it fits this walkthrough and unit tests, not production.

## Accept a Command

```csharp
var inbox = host.Services.GetRequiredService<IInbox>();

var receipt = await inbox.AcceptAsync(
    new ProcessPaymentCommand(Guid.NewGuid(), 25.00m),
    CancellationToken.None).ConfigureAwait(false);

Console.WriteLine($"Accepted {receipt.Id} as {receipt.Outcome}.");
```

The receipt confirms storage, not execution. The handler has not run yet.

## Run a Processor Pass

In production the hosted loop registered by `EnableInboxProcessor` leases and dispatches on its poll interval. Here, run one pass directly so the walkthrough stays deterministic:

```csharp
var processor = host.Services.GetRequiredService<IInboxProcessor>();

var pass = await processor.ProcessPendingAsync(CancellationToken.None).ConfigureAwait(false);

Console.WriteLine($"Leased {pass.LeasedCount}, succeeded {pass.SucceededCount}.");
```

In-process dispatch deserializes the leased envelope and calls `ICommandMediator.SendAsync`, so the handler above runs inside the pass.

## Observe the Stored Row

```csharp
var manager = host.Services.GetRequiredService<IInboxManager>();

var stored = await manager.GetMessageAsync(receipt.Id, CancellationToken.None).ConfigureAwait(false);

Console.WriteLine($"Row {stored?.Id} is {stored?.Status}.");
```

The row reports `Completed` with its attempt count, lease history, and error fields available on the envelope. `IInboxManager` also pages, requeues, purges, and reports status counts; see [Message States](../operations/message-states.md) for the operator model.

## Swap in PostgreSQL Storage

Replace `UseInMemoryStorage` with the PostgreSQL adapter. Pass one shared `NpgsqlDataSource` when inbox and outbox use the same database:

```csharp
inbox.UsePostgreSqlStorage(pg =>
{
    pg.UseDataSource(dataSource);
});
```

`EnsureSchemaCreationOnStartup` defaults to `true`, so this composition creates schema at startup. Migration-owned production deployments set it to `false` via `UseOptions` and keep validation enabled. See [PostgreSQL Schema Management](../integrations/postgresql-schema-management.md) and [Inbox](inbox.md).

## Swap in AMQP Dispatch

Replace `UseInProcessDispatch` with the AMQP dispatcher and register the root transport once. Each builder registers exactly one dispatcher:

```csharp
liteBus.AddAmqpTransport(new AmqpConnectionOptions
{
    Uri = new Uri(connectionString)
});

liteBus.AddInbox(inbox =>
{
    // Contracts and storage as above.
    inbox.UseAmqpDispatch(dispatch => dispatch.DefaultDestination = "payments.commands");
    inbox.EnableInboxProcessor();
});
```

Leased envelopes now publish to the broker for remote execution instead of running handlers in the caller process. Destination mapping, connection recovery, and ingress live in the [AMQP](../integrations/amqp.md) guide.

## Next

- Contract and registration reference: [Inbox](inbox.md), [Outbox](outbox.md)
- Atomic domain plus durable writes: [Transactional messaging writes](transactional-writes.md)
- Delivery guarantees: [Reliable Messaging Semantics](semantics.md)
