using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using LiteBus.Inbox.Abstractions;
using Xunit;

namespace LiteBus.Analyzers.Tests;

/// <summary>
///     Tests for the <see cref="CommandWithResultScheduledToInboxAnalyzer" /> rule.
/// </summary>
public sealed class CommandWithResultScheduledToInboxAnalyzerTests
{
    /// <summary>
    ///     Verifies that void commands stored in the inbox produce no diagnostic.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task VoidCommandStoredInInbox_ProducesNoDiagnostic()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Inbox.Abstractions;

                              public sealed record ProcessPaymentCommand(int PaymentId) : ICommand;

                              public sealed class PaymentService
                              {
                                  private readonly IInbox _inbox;

                                  public PaymentService(IInbox inbox)
                                  {
                                      _inbox = inbox;
                                  }

                                  public Task ScheduleAsync(ProcessPaymentCommand command, CancellationToken cancellationToken)
                                      => _inbox.AddAsync(command, cancellationToken: cancellationToken);
                              }
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<CommandWithResultScheduledToInboxAnalyzer>(source);
    }

    /// <summary>
    ///     Verifies that commands with results stored in the inbox produce LB1004.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task CommandWithResultStoredInInbox_ProducesDiagnostic()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Inbox.Abstractions;

                              public sealed record CreateUserCommand(string Name) : ICommand<int>;

                              public sealed class UserService
                              {
                                  private readonly IInbox _inbox;

                                  public UserService(IInbox inbox)
                                  {
                                      _inbox = inbox;
                                  }

                                  public Task ScheduleAsync(CreateUserCommand command, CancellationToken cancellationToken)
                                      => {|#0:_inbox.AddAsync(command, cancellationToken: cancellationToken)|};
                              }
                              """;

        return AnalyzerTest.VerifyDiagnosticAsync<CommandWithResultScheduledToInboxAnalyzer>(
            source,
            DiagnosticDescriptors.CommandWithResultScheduledToInbox,
            0,
            "CreateUserCommand",
            "int");
    }
}
