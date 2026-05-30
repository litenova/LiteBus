using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using Xunit;

namespace LiteBus.Analyzers.Tests;

/// <summary>
///     Tests for the <see cref="MissingCommandHandlerAnalyzer" /> rule.
/// </summary>
public sealed class MissingCommandHandlerAnalyzerTests
{
    /// <summary>
    ///     Verifies that a command with a handler produces no diagnostic.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task CommandWithHandler_ProducesNoDiagnostic()
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

        return AnalyzerTest.VerifyNoDiagnosticsAsync<MissingCommandHandlerAnalyzer>(source);
    }

    /// <summary>
    ///     Verifies that a command without a handler produces LB1008.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task CommandWithoutHandler_ProducesDiagnostic()
    {
        const string source = """
                              using LiteBus.Commands.Abstractions;

                              public sealed record {|#0:CreateUserCommand|}(string Name) : ICommand;
                              """;

        return AnalyzerTest.VerifyDiagnosticAsync<MissingCommandHandlerAnalyzer>(
            source,
            DiagnosticDescriptors.MissingCommandHandler,
            0,
            "CreateUserCommand");
    }

    /// <summary>
    ///     Verifies that a derived command covered by a base handler produces no diagnostic.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task DerivedCommandWithBaseHandler_ProducesNoDiagnostic()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;

                              public abstract record BaseCommand : ICommand;

                              public sealed record SpecializedCommand : BaseCommand;

                              public sealed class BaseCommandHandler : ICommandHandler<BaseCommand>
                              {
                                  public Task HandleAsync(BaseCommand command, CancellationToken cancellationToken = default)
                                      => Task.CompletedTask;
                              }
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<MissingCommandHandlerAnalyzer>(source);
    }

    /// <summary>
    ///     Verifies that a command covered by an open generic handler produces no diagnostic.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task CommandWithOpenGenericHandler_ProducesNoDiagnostic()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;

                              public sealed record CreateUserCommand(string Name) : ICommand;

                              public sealed class GenericCommandHandler<TCommand> : ICommandHandler<TCommand>
                                  where TCommand : ICommand
                              {
                                  public Task HandleAsync(TCommand command, CancellationToken cancellationToken = default)
                                      => Task.CompletedTask;
                              }
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<MissingCommandHandlerAnalyzer>(source);
    }
}
