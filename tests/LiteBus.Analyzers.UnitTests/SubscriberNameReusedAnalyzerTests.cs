using Microsoft.CodeAnalysis;

namespace LiteBus.Analyzers.UnitTests;

/// <summary>
///     Tests for the <see cref="SubscriberNameReusedAnalyzer" /> rule.
/// </summary>
public sealed class SubscriberNameReusedAnalyzerTests
{
    /// <summary>
    ///     Verifies that one subscriber name shared by handlers of different events produces LB1027.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task SharedNameAcrossDifferentEvents_ProducesDiagnostic()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Events.Abstractions;
                              using LiteBus.Messaging.Abstractions;

                              [SubscriberName("cash.projection")]
                              public sealed class CashProjectionHandler : IEventHandler<OrderCreatedEvent>
                              {
                                  public Task HandleAsync(OrderCreatedEvent message, CancellationToken cancellationToken = default) => Task.CompletedTask;
                              }

                              [SubscriberName("cash.projection")]
                              public sealed class {|#0:RefundProjectionHandler|} : IEventHandler<OrderRefundedEvent>
                              {
                                  public Task HandleAsync(OrderRefundedEvent message, CancellationToken cancellationToken = default) => Task.CompletedTask;
                              }

                              public sealed record OrderCreatedEvent : IEvent;

                              public sealed record OrderRefundedEvent : IEvent;
                              """;

        return AnalyzerTest.VerifyDiagnosticAsync<SubscriberNameReusedAnalyzer>(
            source,
            DiagnosticDescriptors.SubscriberNameReused,
            0,
            "cash.projection",
            "CashProjectionHandler",
            "RefundProjectionHandler");
    }

    /// <summary>
    ///     Verifies that one subscriber name shared by handlers of the same event produces no diagnostic, because
    ///     composition validation already refuses that duplicate.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task SharedNameForSameEvent_ProducesNoDiagnostic()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Events.Abstractions;
                              using LiteBus.Messaging.Abstractions;

                              [SubscriberName("cash.projection")]
                              public sealed class CashProjectionHandler : IEventHandler<OrderCreatedEvent>
                              {
                                  public Task HandleAsync(OrderCreatedEvent message, CancellationToken cancellationToken = default) => Task.CompletedTask;
                              }

                              [SubscriberName("cash.projection")]
                              public sealed class CashAuditHandler : IEventHandler<OrderCreatedEvent>
                              {
                                  public Task HandleAsync(OrderCreatedEvent message, CancellationToken cancellationToken = default) => Task.CompletedTask;
                              }

                              public sealed record OrderCreatedEvent : IEvent;
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<SubscriberNameReusedAnalyzer>(source);
    }

    /// <summary>
    ///     Verifies that handlers of different events with distinct subscriber names produce no diagnostic.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task DistinctNames_ProducesNoDiagnostic()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Events.Abstractions;
                              using LiteBus.Messaging.Abstractions;

                              [SubscriberName("ledger.order-created.cash-projection")]
                              public sealed class CashProjectionHandler : IEventHandler<OrderCreatedEvent>
                              {
                                  public Task HandleAsync(OrderCreatedEvent message, CancellationToken cancellationToken = default) => Task.CompletedTask;
                              }

                              [SubscriberName("ledger.order-refunded.cash-projection")]
                              public sealed class RefundProjectionHandler : IEventHandler<OrderRefundedEvent>
                              {
                                  public Task HandleAsync(OrderRefundedEvent message, CancellationToken cancellationToken = default) => Task.CompletedTask;
                              }

                              public sealed record OrderCreatedEvent : IEvent;

                              public sealed record OrderRefundedEvent : IEvent;
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<SubscriberNameReusedAnalyzer>(source);
    }

    /// <summary>
    ///     Verifies that a shared subscriber name passed through a <c>const</c> field produces LB1027.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task SharedNameThroughConst_ProducesDiagnostic()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Events.Abstractions;
                              using LiteBus.Messaging.Abstractions;

                              public static class Names
                              {
                                  public const string CashProjection = "cash.projection";
                              }

                              [SubscriberName(Names.CashProjection)]
                              public sealed class CashProjectionHandler : IEventHandler<OrderCreatedEvent>
                              {
                                  public Task HandleAsync(OrderCreatedEvent message, CancellationToken cancellationToken = default) => Task.CompletedTask;
                              }

                              [SubscriberName(Names.CashProjection)]
                              public sealed class {|#0:RefundProjectionHandler|} : IEventHandler<OrderRefundedEvent>
                              {
                                  public Task HandleAsync(OrderRefundedEvent message, CancellationToken cancellationToken = default) => Task.CompletedTask;
                              }

                              public sealed record OrderCreatedEvent : IEvent;

                              public sealed record OrderRefundedEvent : IEvent;
                              """;

        return AnalyzerTest.VerifyDiagnosticAsync<SubscriberNameReusedAnalyzer>(
            source,
            DiagnosticDescriptors.SubscriberNameReused,
            0,
            "cash.projection",
            "CashProjectionHandler",
            "RefundProjectionHandler");
    }

    /// <summary>
    ///     Verifies that a subscriber name on a type implementing no handler interface produces no diagnostic, because
    ///     the attribute is inert there.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task SubscriberNameOnNonHandler_ProducesNoDiagnostic()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Events.Abstractions;
                              using LiteBus.Messaging.Abstractions;

                              [SubscriberName("cash.projection")]
                              public sealed class ProjectionOptions
                              {
                              }

                              [SubscriberName("cash.projection")]
                              public sealed class CashProjectionHandler : IEventHandler<OrderCreatedEvent>
                              {
                                  public Task HandleAsync(OrderCreatedEvent message, CancellationToken cancellationToken = default) => Task.CompletedTask;
                              }

                              public sealed record OrderCreatedEvent : IEvent;
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<SubscriberNameReusedAnalyzer>(source);
    }

    /// <summary>
    ///     Verifies that a handler repeating the first handler's messages is skipped, so the diagnostic names the pair
    ///     that actually introduces the second message type.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task ThirdHandlerIntroducingSecondMessage_NamesFirstAndThird()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Events.Abstractions;
                              using LiteBus.Messaging.Abstractions;

                              [SubscriberName("cash.projection")]
                              public sealed class CashProjectionHandler : IEventHandler<OrderCreatedEvent>
                              {
                                  public Task HandleAsync(OrderCreatedEvent message, CancellationToken cancellationToken = default) => Task.CompletedTask;
                              }

                              [SubscriberName("cash.projection")]
                              public sealed class CashAuditHandler : IEventHandler<OrderCreatedEvent>
                              {
                                  public Task HandleAsync(OrderCreatedEvent message, CancellationToken cancellationToken = default) => Task.CompletedTask;
                              }

                              [SubscriberName("cash.projection")]
                              public sealed class {|#0:RefundProjectionHandler|} : IEventHandler<OrderRefundedEvent>
                              {
                                  public Task HandleAsync(OrderRefundedEvent message, CancellationToken cancellationToken = default) => Task.CompletedTask;
                              }

                              public sealed record OrderCreatedEvent : IEvent;

                              public sealed record OrderRefundedEvent : IEvent;
                              """;

        return AnalyzerTest.VerifyDiagnosticAsync<SubscriberNameReusedAnalyzer>(
            source,
            DiagnosticDescriptors.SubscriberNameReused,
            0,
            "cash.projection",
            "CashProjectionHandler",
            "RefundProjectionHandler");
    }

    /// <summary>
    ///     Verifies that the rule is a warning in <c>LiteBus.Outbox</c> that stays on by default.
    /// </summary>
    [Fact]
    public void Descriptor_IsWarningInOutboxCategoryAndEnabledByDefault()
    {
        Assert.Equal("LiteBus.Outbox", DiagnosticDescriptors.SubscriberNameReused.Category);
        Assert.Equal(DiagnosticSeverity.Warning, DiagnosticDescriptors.SubscriberNameReused.DefaultSeverity);
        Assert.True(DiagnosticDescriptors.SubscriberNameReused.IsEnabledByDefault);
    }
}
