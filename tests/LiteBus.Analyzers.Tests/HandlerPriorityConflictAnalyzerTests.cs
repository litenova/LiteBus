using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;
using Xunit;

namespace LiteBus.Analyzers.Tests;

/// <summary>
///     Tests for the <see cref="HandlerPriorityConflictAnalyzer" /> rule.
/// </summary>
public sealed class HandlerPriorityConflictAnalyzerTests
{
    /// <summary>
    ///     Verifies that handlers with distinct priorities produce no diagnostic.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task HandlersWithDistinctPriorities_ProduceNoDiagnostic()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Messaging.Abstractions;

                              public sealed record CreateUserCommand(string Name) : ICommand;

                              [HandlerPriority(1)]
                              public sealed class ValidationPreHandler : ICommandPreHandler<CreateUserCommand>
                              {
                                  public Task PreHandleAsync(CreateUserCommand command, CancellationToken cancellationToken = default)
                                      => Task.CompletedTask;
                              }

                              [HandlerPriority(2)]
                              public sealed class EnrichmentPreHandler : ICommandPreHandler<CreateUserCommand>
                              {
                                  public Task PreHandleAsync(CreateUserCommand command, CancellationToken cancellationToken = default)
                                      => Task.CompletedTask;
                              }
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<HandlerPriorityConflictAnalyzer>(source);
    }

    /// <summary>
    ///     Verifies that handlers with the same priority produce LB1006.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task HandlersWithSamePriority_ProduceDiagnostic()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Messaging.Abstractions;

                              public sealed record CreateUserCommand(string Name) : ICommand;

                              [HandlerPriority(1)]
                              public sealed class FirstValidationPreHandler : ICommandPreHandler<CreateUserCommand>
                              {
                                  public Task PreHandleAsync(CreateUserCommand command, CancellationToken cancellationToken = default)
                                      => Task.CompletedTask;
                              }

                              [HandlerPriority(1)]
                              public sealed class {|#0:SecondValidationPreHandler|} : ICommandPreHandler<CreateUserCommand>
                              {
                                  public Task PreHandleAsync(CreateUserCommand command, CancellationToken cancellationToken = default)
                                      => Task.CompletedTask;
                              }
                              """;

        return AnalyzerTest.VerifyDiagnosticAsync<HandlerPriorityConflictAnalyzer>(
            source,
            DiagnosticDescriptors.HandlerPriorityConflict,
            0,
            "CreateUserCommand",
            "command pre-handler",
            1,
            "FirstValidationPreHandler",
            "SecondValidationPreHandler");
    }
}
