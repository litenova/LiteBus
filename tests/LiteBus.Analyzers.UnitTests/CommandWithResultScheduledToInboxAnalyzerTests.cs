using LiteBus.Inbox.Abstractions;

namespace LiteBus.Analyzers.UnitTests;

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
                                      => _inbox.AcceptAsync(command, cancellationToken: cancellationToken);
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
                                      => {|#0:_inbox.AcceptAsync(command, cancellationToken: cancellationToken)|};
                              }
                              """;

        return AnalyzerTest.VerifyDiagnosticAsync<CommandWithResultScheduledToInboxAnalyzer>(
            source,
            DiagnosticDescriptors.CommandWithResultScheduledToInbox,
            0,
            "CreateUserCommand",
            "int");
    }

    /// <summary>
    ///     Verifies that explicitly typed <c>AcceptAsync&lt;T&gt;</c> with a command that has a result produces LB1004.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task ExplicitGenericAcceptAsyncWithCommandResult_ProducesDiagnostic()
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
                                      => {|#0:_inbox.AcceptAsync<CreateUserCommand>(command, cancellationToken: cancellationToken)|};
                              }
                              """;

        return AnalyzerTest.VerifyDiagnosticAsync<CommandWithResultScheduledToInboxAnalyzer>(
            source,
            DiagnosticDescriptors.CommandWithResultScheduledToInbox,
            0,
            "CreateUserCommand",
            "int");
    }

    /// <summary>
    ///     Verifies that inbox acceptance through a class implementing <see cref="IInbox" /> is detected semantically.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task CommandWithResultStoredThroughInboxImplementation_ProducesDiagnostic()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Inbox.Abstractions;

                              public interface IOrderInbox : IInbox
                              {
                              }

                              public sealed record CreateUserCommand(string Name) : ICommand<int>;

                              public sealed class UserService
                              {
                                  public Task ScheduleAsync(IOrderInbox inbox, CreateUserCommand command, CancellationToken cancellationToken)
                                      => {|#0:inbox.AcceptAsync(command, cancellationToken: cancellationToken)|};
                              }
                              """;

        return AnalyzerTest.VerifyDiagnosticAsync<CommandWithResultScheduledToInboxAnalyzer>(
            source,
            DiagnosticDescriptors.CommandWithResultScheduledToInbox,
            0,
            "CreateUserCommand",
            "int");
    }

    /// <summary>
    ///     Verifies a result command in an inline heterogeneous batch produces LB1004 at the offending item.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task ResultCommandInCollectionExpressionBatch_ProducesDiagnostic()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Inbox.Abstractions;

                              public sealed record CreateUserCommand(string Name) : ICommand<int>;
                              public sealed record NotifyUserCommand(string Name) : ICommand;

                              public sealed class UserService
                              {
                                  public Task ScheduleAsync(IInbox inbox, CancellationToken cancellationToken)
                                      => inbox.AcceptBatchAsync([
                                          {|#0:InboxAcceptItem.From(new CreateUserCommand("Ada"))|},
                                          InboxAcceptItem.From(new NotifyUserCommand("Ada"))
                                      ], cancellationToken);
                              }
                              """;

        return AnalyzerTest.VerifyDiagnosticAsync<CommandWithResultScheduledToInboxAnalyzer>(
            source,
            DiagnosticDescriptors.CommandWithResultScheduledToInbox,
            0,
            "CreateUserCommand",
            "int");
    }

    /// <summary>
    ///     Verifies LB1004 follows local initializers and collection spreads to their concrete batch items.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task ResultCommandInLocalSpreadBatch_ProducesDiagnostic()
    {
        const string source = """
                              using System.Collections.Generic;
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Inbox.Abstractions;

                              public sealed record CreateUserCommand(string Name) : ICommand<int>;

                              public sealed class UserService
                              {
                                  public Task ScheduleAsync(IInbox inbox, CancellationToken cancellationToken)
                                  {
                                      var source = new[]
                                      {
                                          {|#0:InboxAcceptItem.From(new CreateUserCommand("Ada"))|}
                                      };
                                      IReadOnlyList<InboxAcceptItem> items = [.. source];
                                      return inbox.AcceptBatchAsync([.. items], cancellationToken);
                                  }
                              }
                              """;

        return AnalyzerTest.VerifyDiagnosticAsync<CommandWithResultScheduledToInboxAnalyzer>(
            source,
            DiagnosticDescriptors.CommandWithResultScheduledToInbox,
            0,
            "CreateUserCommand",
            "int");
    }

    /// <summary>
    ///     Verifies result commands passed through an explicitly created array to a transactional inbox produce LB1004.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task ResultCommandInTransactionalArrayBatch_ProducesDiagnostic()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Inbox.Abstractions;

                              public sealed record CreateUserCommand(string Name) : ICommand<int>;

                              public sealed class UserService
                              {
                                  public Task ScheduleAsync(ITransactionalInbox inbox, CancellationToken cancellationToken)
                                      => inbox.AcceptBatchAsync(
                                          new InboxAcceptItem[]
                                          {
                                              {|#0:InboxAcceptItem.From(new CreateUserCommand("Ada"))|}
                                          },
                                          cancellationToken);
                              }
                              """;

        return AnalyzerTest.VerifyDiagnosticAsync<CommandWithResultScheduledToInboxAnalyzer>(
            source,
            DiagnosticDescriptors.CommandWithResultScheduledToInbox,
            0,
            "CreateUserCommand",
            "int");
    }

    /// <summary>
    ///     Verifies LB1004 follows a target-typed list initializer assigned to a local batch variable.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task ResultCommandInLocalListBatch_ProducesDiagnostic()
    {
        const string source = """
                              using System.Collections.Generic;
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Inbox.Abstractions;

                              public sealed record CreateUserCommand(string Name) : ICommand<int>;

                              public sealed class UserService
                              {
                                  public Task ScheduleAsync(IInbox inbox, CancellationToken cancellationToken)
                                  {
                                      List<InboxAcceptItem> items = new()
                                      {
                                          {|#0:InboxAcceptItem.From(new CreateUserCommand("Ada"))|}
                                      };
                                      return inbox.AcceptBatchAsync(items, cancellationToken);
                                  }
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
