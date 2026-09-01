# Queries

- **ID**: `mediator.queries`
- **Name**: Queries
- **Maturity**: GA
- **Summary**: Executes read models through one main handler, including single-result and stream query paths.

## What It Does

`IQueryMediator` supports:
- `QueryAsync` for `IQuery<TResult>`.
- `StreamAsync` for `IStreamQuery<TResult>`.

Both paths use single-handler mediation. They share pre, post, and error stages with commands. Stream queries use `SingleStreamHandlerMediationStrategy`, so the handler returns `IAsyncEnumerable<T>` and pipeline stages still run around that flow.

## Public Surface

```csharp
public sealed record GetProductQuery(Guid Id) : IQuery<ProductDto>;

public sealed class GetProductQueryHandler : IQueryHandler<GetProductQuery, ProductDto>
{
    public Task<ProductDto> HandleAsync(GetProductQuery query, CancellationToken cancellationToken = default)
        => Task.FromResult(new ProductDto(query.Id, "keyboard"));
}

var dto = await queryMediator.QueryAsync(new GetProductQuery(id), cancellationToken);
```

| API | Role |
| --- | --- |
| `IQuery<TResult>` | Single-result query contract |
| `IStreamQuery<TResult>` | Stream query contract |
| `IQueryMediator.QueryAsync<TResult>(...)` | Execute single-result query |
| `IQueryMediator.StreamAsync<TResult>(...)` | Execute stream query |
| `IQueryHandler<TQuery, TResult>` | Main handler for `IQuery<TResult>` |
| `IStreamQueryHandler<TQuery, TResult>` | Main handler for stream query |
| `IQueryPreHandler<TQuery>` / `IQueryValidator<TQuery>` | Pre stage |
| `IQueryPostHandler<TQuery>` / `IQueryPostHandler<TQuery, TResult>` | Post stage |
| `IQueryErrorHandler<TQuery>` / `IQueryErrorHandler<TQuery, TResult>` | Error stage |
| `QueryMediatorExtensions.QueryAsync(..., string tag, ...)` | Single-tag query routing sugar |
| `QueryMediatorExtensions.StreamAsync(..., string tag, ...)` | Single-tag stream routing sugar |

## Packages

- `LiteBus.Queries`
- `LiteBus.Queries.Abstractions`
- `LiteBus.Messaging` (transitive runtime dependency)

## Requires

- `mediator.module-registration`
- `mediator.handler-pipeline`
- `mediator.mediation-settings`

## Invariants

- Exactly one main query handler must resolve after routing filters.
- `QueryAsync` and `StreamAsync` throw `ArgumentNullException` for `null` query arguments.
- Aborted stream queries can return an empty stream when pre-stage aborts.
- Query handlers are intended for side-effect free reads.

## Non-Goals

- Automatic caching, pagination, or projection storage.
- Distributed query fan-out across services.
- Durable query replay through inbox/outbox axes.

## Observability

No dedicated query mediator meter or activity source exists.

Operational alternatives:
- Instrument application query handlers directly.
- Use handler-level logging and timing around `IQueryHandler` implementations.
- Apply analyzer guidance (`LB1003`, `LB1009`, `LB1010`) for query purity and handler registration coverage.

## Test Coverage

### Covered

| Test method | Project |
| --- | --- |
| `Mediating_GetProductQuery_ShouldGoThroughHandlersCorrectly` | `LiteBus.Mediator.UnitTests` |
| `Mediating_StreamProductsQuery_ShouldGoThroughHandlersCorrectly` | `LiteBus.Mediator.UnitTests` |
| `Mediating_StreamQuery_WithNoHandler_ShouldThrowNoHandlerFoundException` | `LiteBus.Mediator.UnitTests` |
| `Mediating_StreamQuery_WithIndirectHandler_ShouldUseBaseTypeHandler` | `LiteBus.Mediator.UnitTests` |
| `QueryAsync_WithNullQuery_ThrowsArgumentNullException` | `LiteBus.Mediator.UnitTests` |
| `StreamAsync_WithNullQuery_ThrowsArgumentNullException` | `LiteBus.Mediator.UnitTests` |

### Untested

- Stream cancellation under heavy asynchronous producer backpressure.
- Query-side telemetry contract stability because no dedicated contract is exposed.

### Out-of-Scope

- Durable read model refresh and outbox/inbox-backed projections.
- External broker query response patterns.

## Deep Docs

- [Query module](../../concepts/queries.md)
- [The handler pipeline](../../concepts/handler-pipeline.md)
- [Execution context](../../concepts/execution-context.md)
