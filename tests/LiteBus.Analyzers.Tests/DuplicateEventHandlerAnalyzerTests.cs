using System.Threading.Tasks;
using LiteBus.Events.Abstractions;
using LiteBus.Messaging.Abstractions;
using Xunit;

namespace LiteBus.Analyzers.Tests;

/// <summary>
///     Tests for the <see cref="DuplicateEventHandlerAnalyzer" /> rule.
/// </summary>
public sealed class DuplicateEventHandlerAnalyzerTests
{
    /// <summary>
    ///     Verifies that event handlers with distinct tags produce no diagnostic.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task EventHandlersWithDistinctTags_ProduceNoDiagnostic()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Events.Abstractions;
                              using LiteBus.Messaging.Abstractions;

                              public sealed record OrderCreatedEvent(int OrderId) : IEvent;

                              [HandlerTag("Reporting")]
                              public sealed class ReportingOrderCreatedHandler : IEventHandler<OrderCreatedEvent>
                              {
                                  public Task HandleAsync(OrderCreatedEvent @event, CancellationToken cancellationToken = default)
                                      => Task.CompletedTask;
                              }

                              [HandlerTag("Notifications")]
                              public sealed class NotificationOrderCreatedHandler : IEventHandler<OrderCreatedEvent>
                              {
                                  public Task HandleAsync(OrderCreatedEvent @event, CancellationToken cancellationToken = default)
                                      => Task.CompletedTask;
                              }
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<DuplicateEventHandlerAnalyzer>(source);
    }

    /// <summary>
    ///     Verifies that event handlers with overlapping routing produce LB1002.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task EventHandlersWithOverlappingRouting_ProduceDiagnostic()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Events.Abstractions;

                              public sealed record OrderCreatedEvent(int OrderId) : IEvent;

                              public sealed class FirstOrderCreatedHandler : IEventHandler<OrderCreatedEvent>
                              {
                                  public Task HandleAsync(OrderCreatedEvent @event, CancellationToken cancellationToken = default)
                                      => Task.CompletedTask;
                              }

                              public sealed class {|#0:SecondOrderCreatedHandler|} : IEventHandler<OrderCreatedEvent>
                              {
                                  public Task HandleAsync(OrderCreatedEvent @event, CancellationToken cancellationToken = default)
                                      => Task.CompletedTask;
                              }
                              """;

        return AnalyzerTest.VerifyDiagnosticAsync<DuplicateEventHandlerAnalyzer>(
            source,
            DiagnosticDescriptors.DuplicateEventHandler,
            0,
            "OrderCreatedEvent",
            "FirstOrderCreatedHandler",
            "SecondOrderCreatedHandler");
    }
}
