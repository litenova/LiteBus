using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using LiteBus.Inbox.Abstractions;
using LiteBus.Queries.Abstractions;
using Xunit;

namespace LiteBus.Analyzers.Tests;

/// <summary>
///     Tests for the <see cref="QueryHandlerImpurityAnalyzer" /> rule.
/// </summary>
public sealed class QueryHandlerImpurityAnalyzerTests
{
    /// <summary>
    ///     Verifies that a pure query handler produces no diagnostic.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task PureQueryHandler_ProducesNoDiagnostic()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Queries.Abstractions;

                              public sealed record GetUserQuery(int UserId) : IQuery<string>;

                              public sealed class GetUserQueryHandler : IQueryHandler<GetUserQuery, string>
                              {
                                  public Task<string> HandleAsync(GetUserQuery query, CancellationToken cancellationToken = default)
                                      => Task.FromResult("user");
                              }
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<QueryHandlerImpurityAnalyzer>(source);
    }

    /// <summary>
    ///     Verifies that a query handler depending on a command mediator produces LB1003.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task QueryHandlerWithCommandMediator_ProducesDiagnostic()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Queries.Abstractions;

                              public sealed record GetUserQuery(int UserId) : IQuery<string>;

                              public sealed class GetUserQueryHandler : IQueryHandler<GetUserQuery, string>
                              {
                                  private readonly ICommandMediator _commandMediator;

                                  public GetUserQueryHandler(ICommandMediator {|#0:commandMediator|})
                                  {
                                      _commandMediator = commandMediator;
                                  }

                                  public Task<string> HandleAsync(GetUserQuery query, CancellationToken cancellationToken = default)
                                      => Task.FromResult("user");
                              }
                              """;

        return AnalyzerTest.VerifyDiagnosticAsync<QueryHandlerImpurityAnalyzer>(
            source,
            DiagnosticDescriptors.QueryHandlerImpurity,
            0,
            "GetUserQueryHandler",
            "LiteBus.Commands.Abstractions.ICommandMediator");
    }

    /// <summary>
    ///     Verifies that a query handler depending on <see cref="LiteBus.Inbox.Abstractions.IInbox" /> produces LB1003.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task QueryHandlerWithInbox_ProducesDiagnostic()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Inbox.Abstractions;
                              using LiteBus.Queries.Abstractions;

                              public sealed record GetUserQuery(int UserId) : IQuery<string>;

                              public sealed class GetUserQueryHandler : IQueryHandler<GetUserQuery, string>
                              {
                                  public GetUserQueryHandler(IInbox {|#0:inbox|})
                                  {
                                  }

                                  public Task<string> HandleAsync(GetUserQuery query, CancellationToken cancellationToken = default)
                                      => Task.FromResult("user");
                              }
                              """;

        return AnalyzerTest.VerifyDiagnosticAsync<QueryHandlerImpurityAnalyzer>(
            source,
            DiagnosticDescriptors.QueryHandlerImpurity,
            0,
            "GetUserQueryHandler",
            "LiteBus.Inbox.Abstractions.IInbox");
    }
}
