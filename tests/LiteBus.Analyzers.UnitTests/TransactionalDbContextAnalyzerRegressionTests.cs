namespace LiteBus.Analyzers.UnitTests;

/// <summary>
///     Regression tests for the transactional storage without <c>DbContext</c> analyzer rules.
/// </summary>
public sealed class TransactionalDbContextAnalyzerRegressionTests
{
    /// <summary>
    ///     Verifies a non-Entity Framework class named <c>DbContext</c> suppresses LB1016.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task TransactionalInboxWithNonEntityFrameworkDbContext_ProducesNoDiagnostic()
    {
        const string source = """
                              using LiteBus.Inbox.Abstractions;

                              namespace App.Data
                              {
                                  public sealed class DbContext
                                  {
                                  }
                              }

                              namespace App.Services
                              {
                                  public sealed class OrderService
                                  {
                                      public OrderService(ITransactionalInboxStore inbox, App.Data.DbContext dbContext)
                                      {
                                      }
                                  }
                              }
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<TransactionalInboxWithoutDbContextAnalyzer>(source);
    }

    /// <summary>
    ///     Verifies a non-Entity Framework class named <c>DbContext</c> suppresses LB1013.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task TransactionalOutboxWithNonEntityFrameworkDbContext_ProducesNoDiagnostic()
    {
        const string source = """
                              using LiteBus.Outbox.Abstractions;

                              namespace App.Data
                              {
                                  public sealed class DbContext
                                  {
                                  }
                              }

                              namespace App.Services
                              {
                                  public sealed class OrderService
                                  {
                                      public OrderService(ITransactionalOutboxStore outbox, App.Data.DbContext dbContext)
                                      {
                                      }
                                  }
                              }
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<TransactionalOutboxWithoutDbContextAnalyzer>(source);
    }
}
