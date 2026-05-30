using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using Xunit;

namespace LiteBus.Analyzers.Tests;

/// <summary>
///     Tests for the <see cref="DuplicateCommandHandlerAnalyzer" /> rule.
/// </summary>
public sealed class DuplicateCommandHandlerAnalyzerTests
{
    /// <summary>
    ///     Verifies that a single command handler produces no diagnostic.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task SingleCommandHandler_ProducesNoDiagnostic()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;

                              public sealed record CreateUserCommand(string Name) : ICommand;

                              public sealed class CreateUserCommandHandler : ICommandHandler<CreateUserCommand>
                              {
                                  public Task HandleAsync(CreateUserCommand command, CancellationToken cancellationToken = default)
                                      => Task.CompletedTask;
                              }
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<DuplicateCommandHandlerAnalyzer>(source);
    }

    /// <summary>
    ///     Verifies that duplicate command handlers produce LB1001.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task DuplicateCommandHandlers_ProduceDiagnostic()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;

                              public sealed record CreateUserCommand(string Name) : ICommand;

                              public sealed class FirstCreateUserCommandHandler : ICommandHandler<CreateUserCommand>
                              {
                                  public Task HandleAsync(CreateUserCommand command, CancellationToken cancellationToken = default)
                                      => Task.CompletedTask;
                              }

                              public sealed class {|#0:SecondCreateUserCommandHandler|} : ICommandHandler<CreateUserCommand>
                              {
                                  public Task HandleAsync(CreateUserCommand command, CancellationToken cancellationToken = default)
                                      => Task.CompletedTask;
                              }
                              """;

        return AnalyzerTest.VerifyDiagnosticAsync<DuplicateCommandHandlerAnalyzer>(
            source,
            DiagnosticDescriptors.DuplicateCommandHandler,
            0,
            "CreateUserCommand",
            "FirstCreateUserCommandHandler",
            "SecondCreateUserCommandHandler");
    }
}
