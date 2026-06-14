namespace LiteBus.Analyzers.UnitTests;

public sealed class TransactionalOutboxWithoutDbContextAnalyzerTests
{
    [Fact]
    public async Task TransactionalOutboxWithoutDbContext_ShouldReportMissingDbContext()
    {
        const string source = """
                              using LiteBus.Outbox.Abstractions;

                              public sealed class OrderService
                              {
                                  public {|#4:OrderService|}(ITransactionalOutboxStore outbox)
                                  {
                                  }
                              }
                              """;

        await AnalyzerTest.VerifyDiagnosticAsync<TransactionalOutboxWithoutDbContextAnalyzer>(
            source,
            DiagnosticDescriptors.TransactionalOutboxWithoutDbContext,
            4,
            "OrderService").ConfigureAwait(false);
    }
}