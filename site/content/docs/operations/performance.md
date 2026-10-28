# Performance Considerations

LiteBus mediation keeps overhead low by resolving handlers once at startup and caching the result. Each call then adds only a registry lookup and DI resolution around handler code. This page names each cost and how to control it. It assumes you understand the pipeline from [The Handler Pipeline](../concepts/handler-pipeline.md).

## Handler Resolution and Caching

When you call `AddLiteBus`, LiteBus analyzes handler and message types once at startup. It stores the relationships between messages and their handlers in `MessageRegistry` dictionaries. Each message type gets one descriptor carrying its pre-filtered handler lists. This avoids repeated reflection lookups at runtime.

When open generic handlers are registered, LiteBus closes them over every matching concrete message type at startup through `MakeGenericType` and constraint checking. LiteBus caches the closed handlers like explicitly registered handlers, so no per-call reflection remains.

Handler instances resolve from the dispatch scope when a mediation runs, not when the registry builds. This keeps startup free of handler construction, which matters in applications with many handlers.

Register all handlers during application startup. LiteBus supports runtime registration, but the registry is built to be composed once.

## Keep Handlers Lean

The core mediation pipeline allocates little per call. The primary sources of allocations in a LiteBus application sit inside the handlers themselves. Profile handler code first.

Your handlers must handle cancellation for responsive and resilient applications.

- Pass the `CancellationToken` down through your business logic and to any async library calls (e.g., database queries, HTTP requests).
- In long-running, CPU-bound handlers, periodically check the token and abort if cancellation is requested.

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

The Event Module supports parallel execution of handlers, which can improve performance for events with many independent subscribers. Set the two `ConcurrencyMode` properties on `EventExecutionSettings`:

- **`PriorityGroupsConcurrencyMode`**: Executes different priority groups concurrently. Parallel mode ignores priority ordering. All groups run at once.
- **`HandlersWithinSamePriorityConcurrencyMode`**: Executes handlers within the same priority group concurrently, in registration order when sequential.

Use parallel execution for I/O-bound event handlers (e.g., sending emails, calling external APIs, updating separate database projections). Unless you have measured the benefit on multi-core hardware, avoid it for CPU-bound handlers. Parallel execution makes the order of operations non-deterministic.

The [Event Module](../concepts/events.md) documentation gives the configuration details.

## Durable Processor Throughput

Inbox and outbox throughput is governed by processor options, not by mediation. `BatchSize` sets how many rows each pass leases. `LeaseDuration` and `LeaseHeartbeatInterval` bound slow dispatches. `DispatcherConcurrency` sets the parallel dispatch workers per processor instance. [Inbox](../reliable-messaging/inbox.md) and [Outbox](../reliable-messaging/outbox.md) hold the options reference. Measure changes with the processor benchmarks below.

## Benchmarking

- Use BenchmarkDotNet to measure your handlers in isolation and find the hot paths to tune.
- Use a profiler (such as the built-in Visual Studio profiler or JetBrains dotTrace) to analyze your application under load and find bottlenecks in the LiteBus pipeline or your own code.

The repository includes `benchmarks/LiteBus.Processor.Benchmarks`. Run it with `dotnet run --project benchmarks/LiteBus.Processor.Benchmarks -c Release` to measure pipelined inbox processor throughput and allocations on the target hardware. CI also runs BenchmarkDotNet discovery so renamed or invalid benchmark entry points fail before release.

## Next

- [The Handler Pipeline](../concepts/handler-pipeline.md)
- [Production Runbook](runbook.md)
- [Inbox](../reliable-messaging/inbox.md) and [Outbox](../reliable-messaging/outbox.md) processor options
