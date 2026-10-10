# Query Module

A query is a request to read state without changing it: fetch a product, list orders, stream a report. This page covers the query side of LiteBus: the two query contracts, the matching handlers, how to send a query, and the pipeline stages a query shares with commands. It is for a developer writing query handlers and assumes you have read the [Command Module](commands.md).

A query follows two rules. It is handled by exactly one handler, and it has no side effects. The single-handler rule is enforced; mediation throws if zero or more than one handler is registered. The no-side-effects rule is a discipline you keep, and `IQuery<T>` documents that intent in the type. Name queries for the data they return: `GetProductByIdQuery`, `ListOpenOrdersQuery`, `SearchProductsQuery`.

## Query Contracts

Every query returns a result. The contract you choose depends on whether the result is a single value or a stream.

### `IQuery<TResult>`

The standard query returns one awaitable result.

```csharp
public sealed record GetProductByIdQuery(Guid ProductId) : IQuery<ProductDto>;
```

### `IStreamQuery<TResult>`

A stream query returns `IAsyncEnumerable<TResult>`, yielding items as they are produced. Use it when the result set is large enough that materializing it all in memory is wasteful, for real-time feeds, or for server-driven paging.

```csharp
public sealed record SearchProductsQuery(string SearchTerm) : IStreamQuery<ProductDto>;
```

## Query Handlers

There is one handler interface per query shape.

| Query | Handler interface | Method |
| --- | --- | --- |
| `IQuery<TResult>` | `IQueryHandler<TQuery, TResult>` | `Task<TResult> HandleAsync(TQuery, CancellationToken)` |
| `IStreamQuery<TResult>` | `IStreamQueryHandler<TQuery, TResult>` | `IAsyncEnumerable<TResult> StreamAsync(TQuery, CancellationToken)` |

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

A stream handler is an iterator. Mark the cancellation token with `[EnumeratorCancellation]` so the token the caller passes flows into the `await foreach`.

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

## Sending a Query

Inject `IQueryMediator`. Call `QueryAsync` for a single result and `StreamAsync` to consume a stream.

```csharp
public interface IQueryMediator
{
    Task<TQueryResult> QueryAsync<TQueryResult>(IQuery<TQueryResult> query, QueryMediationSettings? settings = null, CancellationToken cancellationToken = default);
    IAsyncEnumerable<TQueryResult> StreamAsync<TQueryResult>(IStreamQuery<TQueryResult> query, QueryMediationSettings? settings = null, CancellationToken cancellationToken = default);
}
```

```csharp
[ApiController]
[Route("products")]
public class ProductsController : ControllerBase
{
    private readonly IQueryMediator _queryMediator;

    public ProductsController(IQueryMediator queryMediator) => _queryMediator = queryMediator;

    [HttpGet("{id}")]
    public async Task<ActionResult<ProductDto>> GetById(Guid id)
    {
        var product = await _queryMediator.QueryAsync(new GetProductByIdQuery(id));
        return Ok(product);
    }

    [HttpGet("search")]
    public IAsyncEnumerable<ProductDto> Search([FromQuery] string term)
    {
        return _queryMediator.StreamAsync(new SearchProductsQuery(term));
    }
}
```

Returning the `IAsyncEnumerable<T>` straight from the action lets ASP.NET Core serialize each item as it arrives, so the controller never holds the whole result set.

## Routing and Filtering

Queries support the same tag and predicate routing as commands and events. Pass `QueryMediationSettings` with a `QueryRoutingSettings` instance:

```csharp
var settings = new QueryMediationSettings
{
    Routing = new QueryRoutingSettings
    {
        Tags = ["Admin"],
        HandlerPredicate = d => d.HandlerType.Namespace?.StartsWith("MyApp.Admin") == true
    }
};

var result = await _queryMediator.QueryAsync(new GetUserQuery(userId), settings);
```

Single-tag sugar: `await _queryMediator.QueryAsync(query, "Admin");`. See [Handler filtering](handler-filtering.md).

## The Query Pipeline

A query runs through the same stages as a command: pre-handlers, the single main handler, post-handlers, and error-handlers on failure. The interfaces are `IQueryPreHandler<TQuery>`, `IQueryPostHandler<TQuery, TResult>`, and `IQueryErrorHandler<TQuery>`, with the same method shapes as their command equivalents.

The most useful query-side pattern is read-through caching. A pre-handler checks the cache and, on a hit, aborts the pipeline with the cached value so the main handler never runs:

```csharp
public sealed class ProductCacheLookup : IQueryPreHandler<GetProductByIdQuery>
{
    private readonly IProductCache _cache;

    public ProductCacheLookup(IProductCache cache) => _cache = cache;

    public async Task PreHandleAsync(GetProductByIdQuery query, CancellationToken cancellationToken = default)
    {
        var cached = await _cache.TryGetAsync(query.ProductId, cancellationToken);
        if (cached is not null)
            AmbientExecutionContext.Current.Abort(cached); // short-circuit with the cached result
    }
}
```

`Abort(result)` ends mediation and returns the supplied value to the caller. How abort and the rest of the pipeline behave is on [The Handler Pipeline](handler-pipeline.md) and [Execution Context](execution-context.md).

## Shared Features

These mechanics apply to commands, queries, and events. Each has a dedicated page:

- [Handler Priority](handler-priority.md): order pre- and post-handlers.
- [Handler Filtering](handler-filtering.md): select handlers per call.
- [Execution Context](execution-context.md): share state and abort with a result.
- [Polymorphic Dispatch](polymorphic-dispatch.md): handle a base query type for derived queries.
- [Generic Messages & Handlers](generic-messages-and-handlers.md) and [Open Generic Handlers](open-generic-handlers.md): reusable query handlers and cross-cutting steps such as caching or timing.

## Best Practices

- **No side effects.** A query handler reads. If it writes, it should be a command.
- **Return DTOs, not entities.** Shape the result for the consumer and keep domain types out of the read model.
- **Cache in pre/post-handlers.** Read the cache in a pre-handler and abort on a hit; populate it in a post-handler. The handler stays focused on the data source.
- **Stream large results.** Use `IStreamQuery<T>` so memory stays flat regardless of result size.

## Next
