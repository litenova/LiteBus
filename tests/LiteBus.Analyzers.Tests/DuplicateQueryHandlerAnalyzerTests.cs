using System.Threading.Tasks;
using LiteBus.Queries.Abstractions;
using Xunit;

namespace LiteBus.Analyzers.Tests;

/// <summary>
///     Tests for the <see cref="DuplicateQueryHandlerAnalyzer" /> rule.
/// </summary>
public sealed class DuplicateQueryHandlerAnalyzerTests
{
    /// <summary>
    ///     Verifies that a single query handler produces no diagnostic.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task SingleQueryHandler_ProducesNoDiagnostic()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Queries.Abstractions;

                              public sealed record GetUserQuery(int UserId) : IQuery<string>;

                              public sealed class GetUserQueryHandler : IQueryHandler<GetUserQuery, string>
                              {
                                  public Task<string> HandleAsync(GetUserQuery query, CancellationToken cancellationToken = default)
                                      => Task.FromResult("Ada");
                              }
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<DuplicateQueryHandlerAnalyzer>(source);
    }

    /// <summary>
    ///     Verifies that duplicate query handlers produce LB1010.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task DuplicateQueryHandlers_ProduceDiagnostic()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Queries.Abstractions;

                              public sealed record GetUserQuery(int UserId) : IQuery<string>;

                              public sealed class FirstGetUserQueryHandler : IQueryHandler<GetUserQuery, string>
                              {
                                  public Task<string> HandleAsync(GetUserQuery query, CancellationToken cancellationToken = default)
                                      => Task.FromResult("Ada");
                              }

                              public sealed class {|#0:SecondGetUserQueryHandler|} : IQueryHandler<GetUserQuery, string>
                              {
                                  public Task<string> HandleAsync(GetUserQuery query, CancellationToken cancellationToken = default)
                                      => Task.FromResult("Bob");
                              }
                              """;

        return AnalyzerTest.VerifyDiagnosticAsync<DuplicateQueryHandlerAnalyzer>(
            source,
            DiagnosticDescriptors.DuplicateQueryHandler,
            0,
            "GetUserQuery",
            "FirstGetUserQueryHandler",
            "SecondGetUserQueryHandler");
    }
}
