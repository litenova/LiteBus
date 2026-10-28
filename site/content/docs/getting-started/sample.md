# Sample Application Tour

`LiteBus.Sample` is a runnable ASP.NET Core application that composes command, query, event, inbox, and outbox modules through the nested builder API. It uses in-memory storage and in-process dispatch, so it runs with no database and no broker. This page describes what it composes, how to run it, and which documentation page each part demonstrates.

## Composition

`Program.cs` registers the three semantic modules by scanning the application assembly and enables auditing for commands and queries with a console trail. It adds an inbox and an outbox with in-memory storage, in-process dispatch, and processors polling every 100 milliseconds. Two HTTP endpoints drive it. `POST /payments` accepts a `ProcessPaymentCommand` into the inbox, and `GET /payments/{paymentId}` reads the payment status through a query.

The command handler updates an in-process ledger and enqueues `PaymentProcessed` to the outbox. The outbox processor publishes that event to `PaymentProcessedHandler`.

The in-memory ledger and message stores are process-local and share no atomic transaction. Application state that must commit with an inbox or outbox row needs the PostgreSQL or Entity Framework Core storage adapters and the matching transactional writer. See [Transactional Messaging Writes](../reliable-messaging/transactional-writes.md).

## Run It

From the repository root:

```bash
dotnet run --project samples/LiteBus.Sample
```

Accept a payment:

```http
POST /payments
Content-Type: application/json

{
  "paymentId": "018f7f65-e73c-7f85-b13a-2f00427a2278",
  "amount": 19.95
}
```

Read the status after the inbox processor handles the command:

```http
GET /payments/018f7f65-e73c-7f85-b13a-2f00427a2278
```

The full source is in [`samples/LiteBus.Sample`](https://github.com/litenova/LiteBus/tree/main/samples/LiteBus.Sample).

## Sample Parts and Documentation Pages

| Sample Part | Demonstrated Concept |
| --- | --- |
| `Commands/ProcessPaymentCommand.cs` | [Command Module](../concepts/commands.md): a result-less command |
| `Commands/ProcessPaymentCommandHandler.cs` | [Command Module](../concepts/commands.md): handler logic that enqueues the follow-up event ([Outbox](../reliable-messaging/outbox.md)) |
| `Commands/ProcessPaymentCommandValidator.cs` | [Validation](../concepts/validation.md): failures returned as `Validity`, collected across every validator |
| `Commands/RequireSecondApproverGuard.cs` | [The Handler Pipeline](../concepts/handler-pipeline.md#deciding-whether-the-work-happens): a guard denial reported as `Denied` |
| `Commands/SkipAppliedPaymentShortcut.cs` | [The Handler Pipeline](../concepts/handler-pipeline.md#deciding-whether-the-work-happens): a replay answered as `Answered`, never as a refusal |
| `Queries/GetPaymentStatusQuery.cs`, `GetPaymentStatusQueryHandler.cs`, `PaymentStatus.cs` | [Query Module](../concepts/queries.md): a single-result read returning a DTO |
| `Events/PaymentProcessed.cs`, `Events/PaymentProcessedHandler.cs` | [Event Module](../concepts/events.md): an event published through the outbox to its handler |
| `Auditing/ConsoleAuditTrail.cs` | [Auditing](../concepts/auditing.md): the `IAuditTrail` sink, with commands and queries selected for audit records |
| `Program.cs` inbox and outbox builders | [Inbox](../reliable-messaging/inbox.md) and [Outbox](../reliable-messaging/outbox.md): contracts, in-memory storage, in-process dispatch, enabled processors |
| `Program.cs` endpoints | [Getting Started](README.md): `IInbox.AcceptAsync` for durable acceptance, `IQueryMediator.QueryAsync` for reads |
| `Documentation/ModuleRegistrationSnippet.cs` | [Getting Started](README.md): the registration snippet quoted on that page |

## Next

- [Cheat Sheet](cheat-sheet.md) for the same APIs on one page
- [Inbox](../reliable-messaging/inbox.md) and [Outbox](../reliable-messaging/outbox.md) for durable messaging depth
- [The Handler Pipeline](../concepts/handler-pipeline.md) for the stages the sample exercises
