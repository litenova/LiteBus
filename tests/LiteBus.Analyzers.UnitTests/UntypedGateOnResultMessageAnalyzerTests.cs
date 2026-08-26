namespace LiteBus.Analyzers.UnitTests;

/// <summary>
///     Tests for the <see cref="UntypedGateOnResultMessageAnalyzer" /> rule.
/// </summary>
public sealed class UntypedGateOnResultMessageAnalyzerTests
{
    /// <summary>
    ///     Verifies that an untyped gate over a command that produces no result produces no diagnostic.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task UntypedGateOnVoidCommand_ProducesNoDiagnostic()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Messaging.Abstractions;

                              public sealed record RefundOrderCommand : ICommand;

                              public sealed class RefundOrderGate : ICommandGate<RefundOrderCommand>
                              {
                                  public Task<PipelineDirective> DecideAsync(
                                      RefundOrderCommand message,
                                      CancellationToken cancellationToken = default)
                                      => Task.FromResult(PipelineDirective.Continue);
                              }
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<UntypedGateOnResultMessageAnalyzer>(source);
    }

    /// <summary>
    ///     Verifies that the typed gate over a command that produces a result produces no diagnostic.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task TypedGateOnResultCommand_ProducesNoDiagnostic()
    {
        const string source = """
                              using System;
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Messaging.Abstractions;

                              public sealed record CreateProductCommand : ICommand<Guid>;

                              public sealed class CreateProductGate : ICommandGate<CreateProductCommand, Guid>
                              {
                                  public Task<PipelineDirective<Guid>> DecideAsync(
                                      CreateProductCommand message,
                                      CancellationToken cancellationToken = default)
                                      => Task.FromResult(PipelineDirective<Guid>.Continue);
                              }
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<UntypedGateOnResultMessageAnalyzer>(source);
    }

    /// <summary>
    ///     Verifies that an event gate produces no diagnostic, because an event produces no result.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task EventGate_ProducesNoDiagnostic()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Events.Abstractions;
                              using LiteBus.Messaging.Abstractions;

                              public sealed record OrderPlaced : IEvent;

                              public sealed class OrderPlacedGate : IEventGate<OrderPlaced>
                              {
                                  public Task<PipelineDirective> DecideAsync(
                                      OrderPlaced message,
                                      CancellationToken cancellationToken = default)
                                      => Task.FromResult(PipelineDirective.Continue);
                              }
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<UntypedGateOnResultMessageAnalyzer>(source);
    }

    /// <summary>
    ///     Verifies that an open generic gate produces no diagnostic, because its message type is unknown.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task OpenGenericGate_ProducesNoDiagnostic()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Messaging.Abstractions;

                              public sealed class CommandGate<TCommand> : ICommandGate<TCommand>
                                  where TCommand : ICommand
                              {
                                  public Task<PipelineDirective> DecideAsync(
                                      TCommand message,
                                      CancellationToken cancellationToken = default)
                                      => Task.FromResult(PipelineDirective.Continue);
                              }
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<UntypedGateOnResultMessageAnalyzer>(source);
    }

    /// <summary>
    ///     Verifies that an untyped gate over a command that produces a result produces LB1019.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task UntypedGateOnResultCommand_ProducesDiagnostic()
    {
        const string source = """
                              using System;
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Messaging.Abstractions;

                              public sealed record CreateProductCommand : ICommand<Guid>;

                              public sealed class {|#0:CreateProductGate|} : ICommandGate<CreateProductCommand>
                              {
                                  public Task<PipelineDirective> DecideAsync(
                                      CreateProductCommand message,
                                      CancellationToken cancellationToken = default)
                                      => Task.FromResult(PipelineDirective.ShortCircuit("already created"));
                              }
                              """;

        return AnalyzerTest.VerifyDiagnosticAsync<UntypedGateOnResultMessageAnalyzer>(
            source,
            DiagnosticDescriptors.UntypedGateOnResultMessage,
            0,
            "CreateProductGate",
            "CreateProductCommand",
            "Guid",
            "ICommandGate");
    }

    /// <summary>
    ///     Verifies that an untyped gate over a query produces LB1019 naming the query gate contract.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task UntypedGateOnQuery_ProducesDiagnostic()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Messaging.Abstractions;
                              using LiteBus.Queries.Abstractions;

                              public sealed record GetProductQuery : IQuery<string>;

                              public sealed class {|#0:ProductCacheLookup|} : IMessageGate<GetProductQuery>
                              {
                                  public Task<PipelineDirective> DecideAsync(
                                      GetProductQuery message,
                                      CancellationToken cancellationToken = default)
                                      => Task.FromResult(PipelineDirective.Continue);
                              }
                              """;

        return AnalyzerTest.VerifyDiagnosticAsync<UntypedGateOnResultMessageAnalyzer>(
            source,
            DiagnosticDescriptors.UntypedGateOnResultMessage,
            0,
            "ProductCacheLookup",
            "GetProductQuery",
            "string",
            "IQueryGate");
    }

    /// <summary>
    ///     Verifies that an untyped gate over a stream query produces LB1019 naming the stream query gate contract.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task UntypedGateOnStreamQuery_ProducesDiagnostic()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Messaging.Abstractions;
                              using LiteBus.Queries.Abstractions;

                              public sealed record StreamProductsQuery : IStreamQuery<string>;

                              public sealed class {|#0:StreamProductsGate|} : IMessageGate<StreamProductsQuery>
                              {
                                  public Task<PipelineDirective> DecideAsync(
                                      StreamProductsQuery message,
                                      CancellationToken cancellationToken = default)
                                      => Task.FromResult(PipelineDirective.Continue);
                              }
                              """;

        return AnalyzerTest.VerifyDiagnosticAsync<UntypedGateOnResultMessageAnalyzer>(
            source,
            DiagnosticDescriptors.UntypedGateOnResultMessage,
            0,
            "StreamProductsGate",
            "StreamProductsQuery",
            "string",
            "IStreamQueryGate");
    }
}
