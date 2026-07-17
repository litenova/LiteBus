# LiteBus v6 Sample

This ASP.NET Core sample composes command, query, event, inbox, and outbox modules through the v6 nested builder API. It uses in-memory storage and in-process dispatch so it runs without a database or broker.

Run the application from the repository root:

```bash
dotnet run --project samples/LiteBus.Sample
```

Accept a payment command into the inbox:

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

The command handler updates the in-process ledger and enqueues `PaymentProcessed`. The outbox processor publishes that event to `PaymentProcessedHandler`.

The in-memory ledger and message stores are process-local and do not provide an atomic database transaction. Use the PostgreSQL or Entity Framework Core storage adapters and the matching transactional writer for application state that must commit with an inbox or outbox row. See [Transactional Messaging Writes](../../docs/reliable-messaging/transactional-writes.md).
