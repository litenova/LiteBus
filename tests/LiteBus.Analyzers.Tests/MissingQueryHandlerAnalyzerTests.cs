using System.Threading.Tasks;
using LiteBus.Queries.Abstractions;
using Xunit;

namespace LiteBus.Analyzers.Tests;

/// <summary>
///     Tests for the <see cref="MissingQueryHandlerAnalyzer" /> rule.
/// </summary>
public sealed class MissingQueryHandlerAnalyzerTests
{
    /// <summary>
    ///     Verifies that a query with a handler produces no diagnostic.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task QueryWithHandler_ProducesNoDiagnostic()
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

        return AnalyzerTest.VerifyNoDiagnosticsAsync<MissingQueryHandlerAnalyzer>(source);
    }

    /// <summary>
    ///     Verifies that a query without a handler produces LB1009.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task QueryWithoutHandler_ProducesDiagnostic()
    {
        const string source = """
                              using LiteBus.Queries.Abstractions;

                              public sealed record {|#0:GetUserQuery|}(int UserId) : IQuery<string>;
                              """;

        return AnalyzerTest.VerifyDiagnosticAsync<MissingQueryHandlerAnalyzer>(
            source,
            DiagnosticDescriptors.MissingQueryHandler,
            0,
            "GetUserQuery");
    }
}
