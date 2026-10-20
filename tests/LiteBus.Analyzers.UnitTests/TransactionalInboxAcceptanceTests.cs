using LiteBus.Inbox.Abstractions;

namespace LiteBus.Analyzers.UnitTests;

/// <summary>
///     Tests that LB1004 covers the context-bound transactional inbox and untyped acceptance items.
/// </summary>
public sealed class TransactionalInboxAcceptanceTests
{
    /// <summary>
    ///     Verifies that a command with a result accepted through <see cref="ITransactionalInbox{TContext}" />
    ///     produces LB1004.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task CommandWithResultStoredThroughGenericTransactionalInbox_ProducesDiagnostic()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Inbox.Abstractions;

                              public sealed record CreateUserCommand(string Name) : ICommand<int>;

                              public sealed class AppDbContext
                              {
                              }

                              public sealed class UserService
                              {
                                  public Task<InboxReceipt<CreateUserCommand>> ScheduleAsync(ITransactionalInbox<AppDbContext> inbox, CreateUserCommand command, CancellationToken cancellationToken)
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
    ///     Verifies that a command with a result carried by an untyped acceptance item initializer produces LB1004.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task CommandWithResultInUntypedAcceptItemInitializer_ProducesDiagnostic()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Inbox.Abstractions;

                              public sealed record CreateUserCommand(string Name) : ICommand<int>;

                              public sealed class UserService
                              {
                                  public Task<InboxReceipt> ScheduleAsync(IInbox inbox, CancellationToken cancellationToken)
                                      => {|#0:inbox.AcceptAsync(new InboxAcceptItem { Message = new CreateUserCommand("Ada") }, cancellationToken)|};
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
