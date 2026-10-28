namespace LiteBus.Analyzers.UnitTests;

/// <summary>
///     Regression tests for the <see cref="QueryHandlerImpurityAnalyzer" /> rule.
/// </summary>
public sealed class QueryHandlerImpurityAnalyzerRegressionTests
{
    /// <summary>
    ///     Verifies that a query handler depending on <c>IQueryMediator</c> produces LB1003.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task QueryHandlerWithQueryMediator_ProducesDiagnostic()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Queries.Abstractions;

                              public sealed record GetUserQuery(int UserId) : IQuery<string>;

                              public sealed class GetUserQueryHandler : IQueryHandler<GetUserQuery, string>
                              {
                                  public GetUserQueryHandler(IQueryMediator {|#0:queryMediator|})
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
            "LiteBus.Queries.Abstractions.IQueryMediator");
    }

    /// <summary>
    ///     Verifies that a query handler depending on <c>ITransactionalInbox&lt;TContext&gt;</c> produces LB1003.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task QueryHandlerWithGenericTransactionalInbox_ProducesDiagnostic()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Inbox.Abstractions;
                              using LiteBus.Queries.Abstractions;

                              public sealed class AppDbContext
                              {
                              }

                              public sealed record GetUserQuery(int UserId) : IQuery<string>;

                              public sealed class GetUserQueryHandler : IQueryHandler<GetUserQuery, string>
                              {
                                  public GetUserQueryHandler(ITransactionalInbox<AppDbContext> {|#0:inbox|})
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
            "LiteBus.Inbox.Abstractions.ITransactionalInbox`1");
    }

    /// <summary>
    ///     Verifies that an impure dependency inherited from a base handler class produces LB1003.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task QueryHandlerWithInheritedImpureField_ProducesDiagnostic()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Queries.Abstractions;

                              public sealed record GetUserQuery(int UserId) : IQuery<string>;

                              public abstract class QueryHandlerBase
                              {
                                  protected readonly ICommandMediator {|#0:Mediator|} = null!;
                              }

                              public sealed class GetUserQueryHandler : QueryHandlerBase, IQueryHandler<GetUserQuery, string>
                              {
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
    ///     Verifies that an impure base-class constructor parameter produces LB1003.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task QueryHandlerWithInheritedImpureConstructorParameter_ProducesDiagnostic()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Queries.Abstractions;

                              public sealed record GetUserQuery(int UserId) : IQuery<string>;

                              public abstract class QueryHandlerBase
                              {
                                  protected QueryHandlerBase(ICommandMediator {|#0:mediator|})
                                  {
                                  }
                              }

                              public sealed class GetUserQueryHandler : QueryHandlerBase, IQueryHandler<GetUserQuery, string>
                              {
                                  public GetUserQueryHandler()
                                      : base(null!)
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
            "LiteBus.Commands.Abstractions.ICommandMediator");
    }

    /// <summary>
    ///     Verifies that an ordinary method parameter is not treated as an injected dependency.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task QueryHandlerWithOrdinaryMethodParameter_ProducesNoDiagnostic()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Queries.Abstractions;

                              public sealed record GetUserQuery(int UserId) : IQuery<string>;

                              public sealed class GetUserQueryHandler : IQueryHandler<GetUserQuery, string>
                              {
                                  public Task<string> HandleAsync(GetUserQuery query, CancellationToken cancellationToken = default)
                                      => Task.FromResult("user");

                                  private void Configure(ICommandMediator mediator)
                                  {
                                  }
                              }
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<QueryHandlerImpurityAnalyzer>(source);
    }

    /// <summary>
    ///     Verifies that an impure dependency inherited through a base-class property produces LB1003.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task QueryHandlerWithInheritedImpureProperty_ProducesDiagnostic()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Queries.Abstractions;

                              public sealed record GetUserQuery(int UserId) : IQuery<string>;

                              public abstract class QueryHandlerBase
                              {
                                  protected ICommandMediator {|#0:Mediator|} { get; } = null!;
                              }

                              public sealed class GetUserQueryHandler : QueryHandlerBase, IQueryHandler<GetUserQuery, string>
                              {
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
}
