namespace LiteBus.Analyzers.UnitTests;

/// <summary>
///     Regression tests for the <see cref="UnsupportedOpenGenericHandlerAnalyzer" /> rule.
/// </summary>
public sealed class UnsupportedOpenGenericHandlerAnalyzerRegressionTests
{
    /// <summary>
    ///     Verifies that an open generic handler with three type parameters produces LB1005.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task ArityThreeOpenGenericHandler_ProducesDiagnostic()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;

                              public sealed class {|#0:InvalidLogger|}<TCommand, TContext, TExtra> : ICommandPreHandler<TCommand>
                                  where TCommand : ICommand
                              {
                                  public Task PreHandleAsync(TCommand command, CancellationToken cancellationToken = default)
                                      => Task.CompletedTask;
                              }
                              """;

        return AnalyzerTest.VerifyDiagnosticAsync<UnsupportedOpenGenericHandlerAnalyzer>(
            source,
            DiagnosticDescriptors.UnsupportedOpenGenericHandler,
            0,
            "InvalidLogger",
            3);
    }

    /// <summary>
    ///     Verifies that an arity-2 handler bound by a contract taking both parameters produces no diagnostic.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task SupportedArityTwoOpenGenericHandler_ProducesNoDiagnostic()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;

                              public sealed class CommandLogger<TCommand, TResult> : ICommandHandler<TCommand, TResult>
                                  where TCommand : ICommand<TResult>
                              {
                                  public Task<TResult> HandleAsync(TCommand command, CancellationToken cancellationToken = default)
                                      => Task.FromResult<TResult>(default!);
                              }
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<UnsupportedOpenGenericHandlerAnalyzer>(source);
    }
}
