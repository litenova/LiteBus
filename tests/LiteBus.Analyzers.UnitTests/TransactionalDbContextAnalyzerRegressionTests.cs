namespace LiteBus.Analyzers.UnitTests;

/// <summary>
///     Regression tests for the transactional storage without <c>DbContext</c> analyzer rules.
/// </summary>
public sealed class TransactionalDbContextAnalyzerRegressionTests
{
    /// <summary>
    ///     Verifies a non-Entity Framework class named <c>DbContext</c> does not suppress LB1016.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task TransactionalInboxWithNonEntityFrameworkDbContext_ProducesDiagnostic()
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
                                      public {|#0:OrderService|}(ITransactionalInboxStore inbox, App.Data.DbContext dbContext)
                                      {
                                      }
                                  }
                              }
                              """;

        return AnalyzerTest.VerifyDiagnosticAsync<TransactionalInboxWithoutDbContextAnalyzer>(
            source,
            DiagnosticDescriptors.TransactionalInboxWithoutDbContext,
            0,
            "OrderService");
    }

    /// <summary>
    ///     Verifies a non-Entity Framework class named <c>DbContext</c> does not suppress LB1013.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task TransactionalOutboxWithNonEntityFrameworkDbContext_ProducesDiagnostic()
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
                                      public {|#0:OrderService|}(ITransactionalOutboxStore outbox, App.Data.DbContext dbContext)
                                      {
                                      }
                                  }
                              }
                              """;

        return AnalyzerTest.VerifyDiagnosticAsync<TransactionalOutboxWithoutDbContextAnalyzer>(
            source,
            DiagnosticDescriptors.TransactionalOutboxWithoutDbContext,
            0,
            "OrderService");
    }

    /// <summary>
    ///     Verifies an interface named <c>DbContext</c> does not suppress LB1016.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task TransactionalInboxWithDbContextInterface_ProducesDiagnostic()
    {
        const string source = """
                              using LiteBus.Inbox.Abstractions;

                              namespace App.Data
                              {
                                  public interface DbContext
                                  {
                                  }
                              }

                              namespace App.Services
                              {
                                  public sealed class OrderService
                                  {
                                      public {|#0:OrderService|}(ITransactionalInboxStore inbox, App.Data.DbContext dbContext)
                                      {
                                      }
                                  }
                              }
                              """;

        return AnalyzerTest.VerifyDiagnosticAsync<TransactionalInboxWithoutDbContextAnalyzer>(
            source,
            DiagnosticDescriptors.TransactionalInboxWithoutDbContext,
            0,
            "OrderService");
    }

    /// <summary>
    ///     Verifies an interface named <c>DbContext</c> does not suppress LB1013.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task TransactionalOutboxWithDbContextInterface_ProducesDiagnostic()
    {
        const string source = """
                              using LiteBus.Outbox.Abstractions;

                              namespace App.Data
                              {
                                  public interface DbContext
                                  {
                                  }
                              }

                              namespace App.Services
                              {
                                  public sealed class OrderService
                                  {
                                      public {|#0:OrderService|}(ITransactionalOutboxStore outbox, App.Data.DbContext dbContext)
                                      {
                                      }
                                  }
                              }
                              """;

        return AnalyzerTest.VerifyDiagnosticAsync<TransactionalOutboxWithoutDbContextAnalyzer>(
            source,
            DiagnosticDescriptors.TransactionalOutboxWithoutDbContext,
            0,
            "OrderService");
    }

    /// <summary>
    ///     Verifies a context derived from the Entity Framework Core <c>DbContext</c> suppresses LB1016.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task TransactionalInboxWithEntityFrameworkDbContext_ProducesNoDiagnostic()
    {
        const string source = """
                              using LiteBus.Inbox.Abstractions;

                              namespace Microsoft.EntityFrameworkCore
                              {
                                  public class DbContext
                                  {
                                  }
                              }

                              namespace App.Services
                              {
                                  public sealed class AppDbContext : Microsoft.EntityFrameworkCore.DbContext
                                  {
                                  }

                                  public sealed class OrderService
                                  {
                                      public OrderService(ITransactionalInboxStore inbox, AppDbContext dbContext)
                                      {
                                      }
                                  }
                              }
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<TransactionalInboxWithoutDbContextAnalyzer>(source);
    }

    /// <summary>
    ///     Verifies a context derived from the Entity Framework Core <c>DbContext</c> suppresses LB1013.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task TransactionalOutboxWithEntityFrameworkDbContext_ProducesNoDiagnostic()
    {
        const string source = """
                              using LiteBus.Outbox.Abstractions;

                              namespace Microsoft.EntityFrameworkCore
                              {
                                  public class DbContext
                                  {
                                  }
                              }

                              namespace App.Services
                              {
                                  public sealed class AppDbContext : Microsoft.EntityFrameworkCore.DbContext
                                  {
                                  }

                                  public sealed class OrderService
                                  {
                                      public OrderService(ITransactionalOutboxStore outbox, AppDbContext dbContext)
                                      {
                                      }
                                  }
                              }
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<TransactionalOutboxWithoutDbContextAnalyzer>(source);
    }
}
