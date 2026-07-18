namespace LiteBus.Analyzers.UnitTests;

/// <summary>
///     Tests for the <see cref="TransactionalStorageWithoutInterceptorAnalyzer" /> rule.
/// </summary>
public sealed class TransactionalStorageWithoutInterceptorAnalyzerTests
{
    /// <summary>
    ///     Verifies transactional outbox EF configuration without the interceptor produces LB1015.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task TransactionalOutboxWithoutInterceptor_ProducesDiagnostic()
    {
        const string source = """
                              namespace LiteBus.Outbox.Storage.EntityFrameworkCore;

                              public sealed class EfCoreOutboxStorageModuleBuilder
                              {
                                  public EfCoreOutboxStorageModuleBuilder EnforceTransactionalSetup() => this;
                                  public EfCoreOutboxStorageModuleBuilder EnableSaveChangesInterceptor() => this;
                              }

                              public static class OutboxStorageConfiguration
                              {
                                  public static void Configure(EfCoreOutboxStorageModuleBuilder builder)
                                      => {|#0:builder.EnforceTransactionalSetup()|};
                              }
                              """;

        return AnalyzerTest.VerifyDiagnosticAsync<TransactionalStorageWithoutInterceptorAnalyzer>(
            source,
            DiagnosticDescriptors.TransactionalStorageWithoutInterceptor,
            0,
            "Outbox EF Core storage configuration");
    }

    /// <summary>
    ///     Verifies transactional inbox EF configuration with the interceptor produces no diagnostic.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task TransactionalInboxWithInterceptor_ProducesNoDiagnostic()
    {
        const string source = """
                              namespace LiteBus.Inbox.Storage.EntityFrameworkCore;

                              public sealed class EfCoreInboxStorageModuleBuilder
                              {
                                  public EfCoreInboxStorageModuleBuilder EnforceTransactionalSetup() => this;
                                  public EfCoreInboxStorageModuleBuilder EnableSaveChangesInterceptor() => this;
                              }

                              public static class InboxStorageConfiguration
                              {
                                  public static void Configure(EfCoreInboxStorageModuleBuilder builder)
                                  {
                                      builder.EnableSaveChangesInterceptor();
                                      builder.EnforceTransactionalSetup();
                                  }
                              }
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<TransactionalStorageWithoutInterceptorAnalyzer>(source);
    }
}