using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using Xunit;

namespace LiteBus.Analyzers.Tests;

/// <summary>
///     Tests for the <see cref="UnsupportedOpenGenericHandlerAnalyzer" /> rule.
/// </summary>
public sealed class UnsupportedOpenGenericHandlerAnalyzerTests
{
    /// <summary>
    ///     Verifies that a supported open generic handler produces no diagnostic.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task SupportedOpenGenericHandler_ProducesNoDiagnostic()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;

                              public sealed class CommandLogger<TCommand> : ICommandPreHandler<TCommand>
                                  where TCommand : ICommand
                              {
                                  public Task PreHandleAsync(TCommand command, CancellationToken cancellationToken = default)
                                      => Task.CompletedTask;
                              }
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<UnsupportedOpenGenericHandlerAnalyzer>(source);
    }

    /// <summary>
    ///     Verifies that an unsupported open generic handler produces LB1005.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task UnsupportedOpenGenericHandler_ProducesDiagnostic()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;

                              public sealed class {|#0:InvalidLogger|}<TCommand, TContext> : ICommandPreHandler<TCommand>
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
            2);
    }
}
