namespace LiteBus.Analyzers.Tests;

/// <summary>
///     Tests for the <see cref="TransactionalInboxWithoutDbContextAnalyzer" /> rule.
/// </summary>
public sealed class TransactionalInboxWithoutDbContextAnalyzerTests
{
    /// <summary>
    ///     Verifies transactional inbox injection without a DbContext produces LB1016.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task TransactionalInboxWithoutDbContext_ProducesDiagnostic()
    {
        const string source = """
                              using LiteBus.Inbox.Abstractions;

                              public sealed class OrderService
                              {
                                  public {|#0:OrderService|}(ITransactionalInboxStore inbox)
                                  {
                                  }
                              }
                              """;

        return AnalyzerTest.VerifyDiagnosticAsync<TransactionalInboxWithoutDbContextAnalyzer>(
            source,
            DiagnosticDescriptors.TransactionalInboxWithoutDbContext,
            0,
            "OrderService");
    }
}
