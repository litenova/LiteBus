# Performance Considerations

LiteBus resolves handlers once at startup and caches the result. Runtime mediation adds a registry lookup and DI resolution around handler code. This page names each cost and how to control it. It assumes you understand the pipeline from [The Handler Pipeline](../concepts/handler-pipeline.md).

## Handler Resolution and Caching

Handler and message types are analyzed once at startup when you call `AddLiteBus`. The relationships between messages and their handlers are stored in `MessageRegistry` dictionaries, with one descriptor per message type carrying its pre-filtered handler lists. This avoids repeated reflection lookups at runtime.

When open generic handlers are registered, LiteBus closes them over every matching concrete message type at startup through `MakeGenericType` and constraint checking. The resulting closed handlers are cached exactly like explicitly registered handlers, so closed generic handlers take the same cached path as concrete handlers and no per-call reflection remains.

Handler instances resolve from the dispatch scope when a mediation runs, not when the registry builds. This keeps startup free of handler construction, which matters in applications with many handlers.

**Recommendation**: Register all your handlers during application startup. While LiteBus supports runtime registration, the registry is built to be composed once.

## Keep Handlers Lean

The core mediation pipeline allocates little per call. The primary sources of allocations in a LiteBus application sit inside the handlers themselves, so profile handler code first.

Properly handling cancellation is required for building responsive and resilient applications.

- **Propagate `CancellationToken`**: Always pass the `CancellationToken` down through your business logic and to any async library calls (e.g., database queries, HTTP requests).
- **Check for Cancellation**: In long-running, CPU-bound handlers, periodically check the token and abort if cancellation is requested.

```csharp
public async Task HandleAsync(ProcessLargeFileCommand command, CancellationToken cancellationToken = default)
{
    foreach (var line in command.Lines)
    {
        // Check for cancellation before processing each item.
        cancellationToken.ThrowIfCancellationRequested();

        // Simulate work
        await Task.Delay(10, cancellationToken).ConfigureAwait(false);
    }
}
```

## Concurrency in the Event Module

The Event Module allows for parallel execution of handlers, which can improve performance for events with many independent subscribers. Set the two `ConcurrencyMode` properties on `EventExecutionSettings`:

- **`PriorityGroupsConcurrencyMode`**: Executes different priority groups concurrently. Parallel mode ignores priority ordering: all groups run at once.
- **`HandlersWithinSamePriorityConcurrencyMode`**: Executes handlers within the same priority group concurrently, in registration order when sequential.

**Recommendation**: Use parallel execution for I/O-bound event handlers (e.g., sending emails, calling external APIs, updating separate database projections). Avoid it for CPU-bound handlers unless you have measured the benefit on multi-core hardware. Parallel execution makes the order of operations non-deterministic.

See the [Event Module](../concepts/events.md) documentation for configuration details.

## Durable Processor Throughput

Inbox and outbox throughput is governed by processor options, not by mediation: `BatchSize` sets how many rows each pass leases, `LeaseDuration` and `LeaseHeartbeatInterval` bound slow dispatches, and `DispatcherConcurrency` sets the parallel dispatch workers per processor instance. See [Inbox](../reliable-messaging/inbox.md) and [Outbox](../reliable-messaging/outbox.md) for the options reference, and measure changes with the processor benchmarks below.

## Benchmarking

- **BenchmarkDotNet**: Use BenchmarkDotNet to measure the performance of your handlers in isolation. This can help you identify and tune hot paths.
- **Profiling**: Use a profiler (like Visual Studio's built-in profiler or JetBrains dotTrace) to analyze the performance of your application under load and identify any bottlenecks within the LiteBus pipeline or your own code.

The repository includes `benchmarks/LiteBus.Processor.Benchmarks`. Run it with `dotnet run --project benchmarks/LiteBus.Processor.Benchmarks -c Release` to measure pipelined inbox processor throughput and allocations on the target hardware. CI also runs BenchmarkDotNet discovery so renamed or invalid benchmark entry points fail before release.

## Next

- [The Handler Pipeline](../concepts/handler-pipeline.md)
- [Production Runbook](runbook.md)
- [Inbox](../reliable-messaging/inbox.md) and [Outbox](../reliable-messaging/outbox.md) processor options
