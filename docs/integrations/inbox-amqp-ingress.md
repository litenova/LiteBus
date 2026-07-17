# Inbox AMQP Ingress

AMQP inbox ingress consumes commands from a broker queue and accepts them into the inbox store through `IInbox.AcceptAsync`. Pair with nested storage, `UseInProcessDispatch`, and `EnableInboxProcessor`.

RabbitMQ and LavinMQ both speak AMQP 0.9.1 through `LiteBus.Transport.Amqp`.

## Flow

1. Upstream publisher sends JSON with LiteBus AMQP headers.
2. `TransportInboxIngressConsumer` runs the subscription loop.
3. `AmqpInboxIngressHandler` deserializes and calls `IInbox.AcceptAsync`.
4. Consumer acknowledges after store acceptance.
5. `PipelinedInboxProcessor` leases and dispatches through `IInboxDispatcher`.

## Registration

```csharp
builder.Services.AddLiteBus(builder =>
{
    builder.AddAmqpTransport(new AmqpConnectionOptions
    {
        Uri = new Uri(configuration.GetConnectionString("Amqp")!)
    });
    builder.AddMessaging(_ => { });
    builder.AddCommands(commands =>
        commands.RegisterFromAssembly(typeof(Program).Assembly));

    builder.AddInbox(inbox =>
    {
        inbox.Contracts.Register<ProcessPaymentCommand>("payments.process-payment", 1);
        inbox.UsePostgreSqlStorage(pg => pg.UseDataSource(dataSource));
        inbox.UseInProcessDispatch();
        inbox.UseAmqpIngress(ingress =>
        {
            ingress.UseOptions(new AmqpInboxIngressOptions
            {
                QueueName = "commands.inbox"
            });
        });
        inbox.EnableInboxProcessor(host => host.PollInterval = TimeSpan.FromSeconds(1));
    });
});
```

Ingress requires the root `AmqpTransportModule` registered by `AddAmqpTransport`. Broker connection settings do not belong to ingress options.

## Headers

Publishers must send `litebus-contract-name` and `litebus-contract-version`. Ingress defaults to broker-scoped idempotency from the AMQP `message-id` (or `litebus-message-id` header) so redelivery after a successful accept does not create duplicate inbox rows.

Optional `litebus-idempotency-key` and `tenant-id` headers are ignored unless `TransportInboxIngressOptions.TrustApplicationHeaders` is `true` on an authenticated broker binding. Use `AuthorizeDeliveryAsync` to enforce tenant or contract policy before accept.

Configure `MaxMessageBytes` to cap ingress body size (default 4 MiB). Oversized deliveries throw `InboxIngressException` and are discarded unless `RequeueOnFailure` applies to the failure type.

See [Inbox](../reliable-messaging/inbox.md) and [AMQP transport](amqp.md).
