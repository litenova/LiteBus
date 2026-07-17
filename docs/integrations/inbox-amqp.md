# Inbox AMQP Patterns

Two AMQP patterns cover most command flows:

| Direction | Package | Role |
| --- | --- | --- |
| Ingress (consume into inbox) | `LiteBus.Inbox.Ingress.Amqp` | Broker queue to `IInbox.AcceptAsync` |
| Dispatch (publish leased inbox) | `LiteBus.Inbox.Dispatch.Amqp` | `PipelinedInboxProcessor` to the explicitly registered root AMQP transport |

## Local Development Without a Broker

```csharp
inbox.UseInMemoryStorage();
inbox.UseInProcessDispatch();
```

## Remote Ingress, Local Execution

```text
External producer to AMQP queue to UseAmqpIngress to IInbox.AcceptAsync to store
  to PipelinedInboxProcessor to UseInProcessDispatch to ICommandMediator
```

## Remote Execution (Inbox as Outbound Command Bus)

```text
IInbox.AcceptAsync to store to PipelinedInboxProcessor to UseAmqpDispatch to AMQP exchange
  to remote service UseAmqpIngress to IInbox.AcceptAsync
```

See [Inbox AMQP Ingress](inbox-amqp-ingress.md) and [AMQP Transport](amqp.md).
