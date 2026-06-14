namespace LiteBus.Analyzers.UnitTests;

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
                                  private readonly ICommandMediator {|#0:_commandMediator|};

                                  public GetUserQueryHandler(ICommandMediator commandMediator)
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

    /// <summary>
    ///     Verifies that a query handler depending on <c>ITransactionalInbox</c> produces LB1003.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task QueryHandlerWithTransactionalInbox_ProducesDiagnostic()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Inbox.Abstractions;
                              using LiteBus.Queries.Abstractions;

                              public sealed record GetUserQuery(int UserId) : IQuery<string>;

                              public sealed class GetUserQueryHandler : IQueryHandler<GetUserQuery, string>
                              {
                                  public GetUserQueryHandler(IInboxStore {|#0:store|})
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
            "LiteBus.Inbox.Abstractions.IInboxStore");
    }

    /// <summary>
    ///     Verifies that a query handler depending on <c>IMessageTransport</c> produces LB1003.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task StreamQueryHandlerWithCommandMediator_ProducesDiagnostic()
    {
        const string source = """
                              using System.Collections.Generic;
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Queries.Abstractions;

                              public sealed record StreamUsersQuery : IStreamQuery<string>;

                              public sealed class StreamUsersQueryHandler : IStreamQueryHandler<StreamUsersQuery, string>
                              {
                                  private readonly ICommandMediator {|#0:_commandMediator|};

                                  public StreamUsersQueryHandler(ICommandMediator commandMediator)
                                  {
                                      _commandMediator = commandMediator;
                                  }

                                  public IAsyncEnumerable<string> StreamAsync(StreamUsersQuery query, CancellationToken cancellationToken = default)
                                      => Empty();

                                  private static async IAsyncEnumerable<string> Empty()
                                  {
                                      yield break;
                                  }
                              }
                              """;

        return AnalyzerTest.VerifyDiagnosticAsync<QueryHandlerImpurityAnalyzer>(
            source,
            DiagnosticDescriptors.QueryHandlerImpurity,
            0,
            "StreamUsersQueryHandler",
            "LiteBus.Commands.Abstractions.ICommandMediator");
    }

    /// <summary>
    ///     Verifies that impure dependencies on handler fields produce LB1003.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task QueryHandlerWithImpureField_ProducesDiagnostic()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Events.Abstractions;
                              using LiteBus.Queries.Abstractions;

                              public sealed record GetUserQuery(int UserId) : IQuery<string>;

                              public sealed class GetUserQueryHandler : IQueryHandler<GetUserQuery, string>
                              {
                                  private readonly IEventMediator {|#0:eventMediator|};

                                  public Task<string> HandleAsync(GetUserQuery query, CancellationToken cancellationToken = default)
                                      => Task.FromResult("user");
                              }
                              """;

        return AnalyzerTest.VerifyDiagnosticAsync<QueryHandlerImpurityAnalyzer>(
            source,
            DiagnosticDescriptors.QueryHandlerImpurity,
            0,
            "GetUserQueryHandler",
            "LiteBus.Events.Abstractions.IEventMediator");
    }

    /// <summary>
    ///     Verifies that a query handler depending on <c>IMessageTransport</c> produces LB1003.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task QueryHandlerWithMessageTransport_ProducesDiagnostic()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Queries.Abstractions;
                              using LiteBus.Transport.Abstractions;

                              public sealed record GetUserQuery(int UserId) : IQuery<string>;

                              public sealed class GetUserQueryHandler : IQueryHandler<GetUserQuery, string>
                              {
                                  public GetUserQueryHandler(IMessageTransport {|#0:transport|})
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
            "LiteBus.Transport.Abstractions.IMessageTransport");
    }
}