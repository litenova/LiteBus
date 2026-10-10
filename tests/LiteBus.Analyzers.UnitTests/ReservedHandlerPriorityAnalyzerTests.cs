namespace LiteBus.Analyzers.UnitTests;

/// <summary>
///     Tests for the <see cref="ReservedHandlerPriorityAnalyzer" /> rule.
/// </summary>
public sealed class ReservedHandlerPriorityAnalyzerTests
{
    /// <summary>
    ///     Verifies that a handler with no priority attribute produces no diagnostic.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task HandlerWithNoPriority_ProducesNoDiagnostic()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;

                              public sealed record ShipOrderCommand : ICommand;

                              public sealed class ShipOrderHandler : ICommandHandler<ShipOrderCommand>
                              {
                                  public Task HandleAsync(ShipOrderCommand message, CancellationToken cancellationToken = default)
                                      => Task.CompletedTask;
                              }
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<ReservedHandlerPriorityAnalyzer>(source);
    }

    /// <summary>
    ///     Verifies that an application priority below the reserved floor produces no diagnostic.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task PriorityBelowTheReservedFloor_ProducesNoDiagnostic()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Messaging.Abstractions;

                              public sealed record ShipOrderCommand : ICommand;

                              [HandlerPriority(10)]
                              public sealed class ShipOrderHandler : ICommandHandler<ShipOrderCommand>
                              {
                                  public Task HandleAsync(ShipOrderCommand message, CancellationToken cancellationToken = default)
                                      => Task.CompletedTask;
                              }
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<ReservedHandlerPriorityAnalyzer>(source);
    }

    /// <summary>
    ///     Verifies that a priority at or above the reserved ceiling produces no diagnostic, because that band is the
    ///     documented place for application infrastructure that must run after every LiteBus handler.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task PriorityAtTheReservedCeiling_ProducesNoDiagnostic()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Messaging.Abstractions;

                              public sealed record ShipOrderCommand : ICommand;

                              [HandlerPriority(HandlerPriorities.UnitOfWork)]
                              public sealed class CommitUnitOfWork : ICommandHandler<ShipOrderCommand>
                              {
                                  public Task HandleAsync(ShipOrderCommand message, CancellationToken cancellationToken = default)
                                      => Task.CompletedTask;
                              }
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<ReservedHandlerPriorityAnalyzer>(source);
    }

    /// <summary>
    ///     Verifies that reaching for a LiteBus stage constant to order two application handlers is reported.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task PriorityInsideTheReservedRange_ProducesDiagnostic()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Messaging.Abstractions;

                              public sealed record ShipOrderCommand : ICommand;

                              [{|#0:HandlerPriority(HandlerPriorities.Persistence)|}]
                              public sealed class ShipOrderHandler : ICommandHandler<ShipOrderCommand>
                              {
                                  public Task HandleAsync(ShipOrderCommand message, CancellationToken cancellationToken = default)
                                      => Task.CompletedTask;
                              }
                              """;

        return AnalyzerTest.VerifyDiagnosticAsync<ReservedHandlerPriorityAnalyzer>(
            source,
            DiagnosticDescriptors.ReservedHandlerPriority,
            0,
            "ShipOrderHandler",
            1_000_100);
    }

    /// <summary>
    ///     Verifies that the reserved floor itself is reported, since it is the boundary of the window rather than an
    ///     application position.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task PriorityOnTheReservedFloor_ProducesDiagnostic()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Messaging.Abstractions;

                              public sealed record ShipOrderCommand : ICommand;

                              [{|#0:HandlerPriority(HandlerPriorities.ReservedFloor)|}]
                              public sealed class ShipOrderHandler : ICommandHandler<ShipOrderCommand>
                              {
                                  public Task HandleAsync(ShipOrderCommand message, CancellationToken cancellationToken = default)
                                      => Task.CompletedTask;
                              }
                              """;

        return AnalyzerTest.VerifyDiagnosticAsync<ReservedHandlerPriorityAnalyzer>(
            source,
            DiagnosticDescriptors.ReservedHandlerPriority,
            0,
            "ShipOrderHandler",
            1_000_000);
    }
}
