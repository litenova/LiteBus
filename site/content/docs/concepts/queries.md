# Query Module

A query asks LiteBus to read state without changing it. This page shows you how to define a query, handle it, send it, and short-circuit it with cached answers.

> **This page is for** developers who read state through LiteBus.
>
> **After reading it** you can define, handle, and send single-result and stream queries. Continue with [Events](events.md).

## Define a Query

```csharp
public sealed record GetProductByIdQuery(Guid ProductId) : IQuery<ProductDto>;

public sealed record SearchProductsQuery(string SearchTerm) : IStreamQuery<ProductDto>;
```

Every query returns a result. Use `IQuery<TResult>` for one awaitable value and `IStreamQuery<TResult>` for an async stream of values. Pick the stream shape when the result set is large enough that holding it all in memory is wasteful, for real-time feeds, or for server-driven paging.

Two rules define a query. It is handled by exactly one handler, and it has no side effects. Mediation throws `NoHandlerFoundException` when no handler survives filtering and `MultipleHandlerFoundException` when more than one handler at the winning level survives: direct handlers win whenever any survive, and handlers registered for a base type apply only when no direct handler does. The no-side-effects rule is a discipline you keep: when the handler writes, model the operation as a command instead. Name queries for the data they return, such as `GetProductByIdQuery` or `ListOpenOrdersQuery`.

## Handle a Query

```csharp
public sealed class GetProductByIdQueryHandler : IQueryHandler<GetProductByIdQuery, ProductDto>
{
    private readonly IProductRepository _repository;

    public GetProductByIdQueryHandler(IProductRepository repository) => _repository = repository;

    public async Task<ProductDto> HandleAsync(GetProductByIdQuery query, CancellationToken cancellationToken = default)
    {
        var product = await _repository.GetAsync(query.ProductId, cancellationToken);
        return new ProductDto(product.Id, product.Name, product.Price);
    }
}
```

| Query | Handler interface | Method |
| --- | --- | --- |
| `IQuery<TResult>` | `IQueryHandler<TQuery, TResult>` | `Task<TResult> HandleAsync(TQuery, CancellationToken)` |
| `IStreamQuery<TResult>` | `IStreamQueryHandler<TQuery, TResult>` | `IAsyncEnumerable<TResult> StreamAsync(TQuery, CancellationToken)` |

There is one handler interface per query shape. Return DTOs shaped for the consumer and keep domain types out of the read model.

```csharp
public sealed class SearchProductsQueryHandler : IStreamQueryHandler<SearchProductsQuery, ProductDto>
{
    private readonly IProductRepository _repository;

    public SearchProductsQueryHandler(IProductRepository repository) => _repository = repository;

    public async IAsyncEnumerable<ProductDto> StreamAsync(
        SearchProductsQuery query,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var product in _repository.SearchAsync(query.SearchTerm, cancellationToken))
        {
            yield return new ProductDto(product.Id, product.Name, product.Price);
        }
    }
}
```

Write a stream handler as an iterator and mark the token with `[EnumeratorCancellation]` so the token the caller passes flows into the `await foreach`.

## Send a Query

```csharp
public async Task<ActionResult<ProductDto>> GetById(
    Guid id,
    [FromServices] IQueryMediator queryMediator,
    CancellationToken cancellationToken)
{
    var product = await queryMediator.QueryAsync(new GetProductByIdQuery(id), cancellationToken: cancellationToken);
    return Ok(product);
}
```

Inject `IQueryMediator` and call `QueryAsync` for a single result or `StreamAsync` to consume a stream. The signatures are:

```csharp
Task<TQueryResult> QueryAsync<TQueryResult>(IQuery<TQueryResult> query, QueryMediationSettings? queryMediationSettings = null, CancellationToken cancellationToken = default);
IAsyncEnumerable<TQueryResult> StreamAsync<TQueryResult>(IStreamQuery<TQueryResult> query, QueryMediationSettings? queryMediationSettings = null, CancellationToken cancellationToken = default);
```

Enumerate a returned stream once and dispose its enumerator when you stop early. The stream holds its dispatch scope until it is enumerated to completion or disposed, so a stream you abandon keeps its scoped handler dependencies alive. Returning the stream straight from an ASP.NET Core action lets the framework serialize each item as it arrives, so the controller never holds the whole result set.

## Filter Which Handlers Run

```csharp
var settings = new QueryMediationSettings
{
    Routing = new QueryRoutingSettings
    {
        Tags = ["Admin"],
        HandlerPredicate = d => d.HandlerType.Namespace?.StartsWith("MyApp.Admin") == true
    }
};

var result = await queryMediator.QueryAsync(new GetUserQuery(userId), settings, cancellationToken);
```

Pass `QueryMediationSettings` when the same query must run a different set of handlers per call. `Routing.Tags` keeps only handlers with a matching tag and `Routing.HandlerPredicate` filters on the handler descriptor after tags. For one tag use the sugar overload: `await queryMediator.QueryAsync(query, "Admin", cancellationToken)`. See [Handler Filtering](handler-filtering.md).

## Answer From a Shortcut

```csharp
public sealed class ProductCacheLookup : IQueryShortcut<GetProductByIdQuery, ProductDto>
{
    private readonly IProductCache _cache;

    public ProductCacheLookup(IProductCache cache) => _cache = cache;

    public async Task<Shortcut<ProductDto>> TryAnswerAsync(
        GetProductByIdQuery query,
        CancellationToken cancellationToken = default)
    {
        var cached = await _cache.TryGetAsync(query.ProductId, cancellationToken);

        return cached is null
            ? Shortcut<ProductDto>.None
            : Shortcut<ProductDto>.Answer(cached, "served from cache");
    }
}
```

Use a shortcut for answers you already hold, such as a cache hit. The answer is typed over the query result, so the compiler requires the right shape. An answered query reports `MediationOutcome.Answered`, which an audit trail records as a success because nothing was refused.

Refusing a read is a different job. Implement `IQueryGuard<TQuery>` and return `Verdict.Deny(reason)`, which reports `MediationOutcome.Denied`. LiteBus runs every guard before any shortcut, so a cached answer can never reach a caller a guard would have refused.

## Details

- The full stage order, including guards, validators, shortcuts, post-handlers, error-handlers, and completion handlers, lives on [The Handler Pipeline](handler-pipeline.md). That page is authoritative when this page and that page seem to differ.
- When a refusal is a routine branch rather than a fault, call `TryQueryAsync` and read the outcome value instead of catching. When you need to ask whether a read would be permitted without running it, call `EvaluateAsync`. See [Refusal as a Value](handler-pipeline.md#refusal-as-a-value).

## Next

- [Events](events.md) for publish-subscribe with zero to many handlers
- [The Handler Pipeline](handler-pipeline.md) for guards, shortcuts, and caching order
- [Execution Context](execution-context.md) for state shared across one call
