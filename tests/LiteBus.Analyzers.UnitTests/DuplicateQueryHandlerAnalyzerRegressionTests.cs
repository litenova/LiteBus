namespace LiteBus.Analyzers.UnitTests;

/// <summary>
///     Regression tests for the <see cref="DuplicateQueryHandlerAnalyzer" /> rule.
/// </summary>
public sealed class DuplicateQueryHandlerAnalyzerRegressionTests
{
    /// <summary>
    ///     Verifies that duplicate stream query handlers produce LB1010.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task DuplicateStreamQueryHandlers_ProduceDiagnostic()
    {
        const string source = """
                              using System.Collections.Generic;
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Queries.Abstractions;

                              public sealed record StreamUsersQuery : IStreamQuery<string>;

                              public sealed class FirstStreamUsersQueryHandler : IStreamQueryHandler<StreamUsersQuery, string>
                              {
                                  public IAsyncEnumerable<string> StreamAsync(StreamUsersQuery query, CancellationToken cancellationToken = default)
                                      => Empty();

                                  private static async IAsyncEnumerable<string> Empty()
                                  {
                                      yield break;
                                  }
                              }

                              public sealed class {|#0:SecondStreamUsersQueryHandler|} : IStreamQueryHandler<StreamUsersQuery, string>
                              {
                                  public IAsyncEnumerable<string> StreamAsync(StreamUsersQuery query, CancellationToken cancellationToken = default)
                                      => Empty();

                                  private static async IAsyncEnumerable<string> Empty()
                                  {
                                      yield break;
                                  }
                              }
                              """;

        return AnalyzerTest.VerifyDiagnosticAsync<DuplicateQueryHandlerAnalyzer>(
            source,
            DiagnosticDescriptors.DuplicateQueryHandler,
            0,
            "StreamUsersQuery",
            "FirstStreamUsersQueryHandler",
            "SecondStreamUsersQueryHandler");
    }
}
