namespace LiteBus.Analyzers.Tests;

/// <summary>
///     Tests for the <see cref="ProcessorEnabledWithoutDispatcherAnalyzer" /> rule.
/// </summary>
public sealed class ProcessorEnabledWithoutDispatcherAnalyzerTests
{
    /// <summary>
    ///     Verifies inbox processor enablement without a dispatcher produces LB1014.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task InboxProcessorWithoutDispatcher_ProducesDiagnostic()
    {
        const string source = """
                              namespace LiteBus.Inbox.Abstractions;

                              public sealed class InboxModuleBuilder
                              {
                                  public InboxModuleBuilder EnableInboxProcessor() => this;
                                  public InboxModuleBuilder RegisterDispatcher(object module) => this;
                              }

                              public static class InboxConfiguration
                              {
                                  public static void Configure(InboxModuleBuilder inbox)
                                      => {|#0:inbox.EnableInboxProcessor()|};
                              }
                              """;

        return AnalyzerTest.VerifyDiagnosticAsync<ProcessorEnabledWithoutDispatcherAnalyzer>(
            source,
            DiagnosticDescriptors.ProcessorEnabledWithoutDispatcher,
            0,
            "Inbox module configuration",
            "Inbox",
            "UseInProcessDispatch");
    }

    /// <summary>
    ///     Verifies inbox processor enablement with a dispatcher in the same scope produces no diagnostic.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task InboxProcessorWithDispatcher_ProducesNoDiagnostic()
    {
        const string source = """
                              namespace LiteBus.Inbox.Abstractions;

                              public sealed class InboxModuleBuilder
                              {
                                  public InboxModuleBuilder EnableInboxProcessor() => this;
                                  public InboxModuleBuilder RegisterDispatcher(object module) => this;
                              }

                              public static class InboxConfiguration
                              {
                                  public static void Configure(InboxModuleBuilder inbox)
                                  {
                                      inbox.RegisterDispatcher(new object());
                                      inbox.EnableInboxProcessor();
                                  }
                              }
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<ProcessorEnabledWithoutDispatcherAnalyzer>(source);
    }

    /// <summary>
    ///     Verifies inbox processor enablement with UseInProcessDispatch in the same scope produces no diagnostic.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task InboxProcessorWithUseInProcessDispatch_ProducesNoDiagnostic()
    {
        const string source = """
                              namespace LiteBus.Inbox.Abstractions;

                              public sealed class InboxModuleBuilder
                              {
                                  public InboxModuleBuilder EnableInboxProcessor() => this;
                                  public InboxModuleBuilder UseInProcessDispatch() => this;
                              }

                              public static class InboxConfiguration
                              {
                                  public static void Configure(InboxModuleBuilder inbox)
                                  {
                                      inbox.UseInProcessDispatch();
                                      inbox.EnableInboxProcessor();
                                  }
                              }
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<ProcessorEnabledWithoutDispatcherAnalyzer>(source);
    }

    /// <summary>
    ///     Verifies outbox processor enablement without a dispatcher produces LB1014.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task OutboxProcessorWithoutDispatcher_ProducesDiagnostic()
    {
        const string source = """
                              namespace LiteBus.Outbox.Abstractions;

                              public sealed class OutboxModuleBuilder
                              {
                                  public OutboxModuleBuilder EnableOutboxProcessor() => this;
                                  public OutboxModuleBuilder RegisterDispatcher(object module) => this;
                              }

                              public static class OutboxConfiguration
                              {
                                  public static void Configure(OutboxModuleBuilder outbox)
                                      => {|#0:outbox.EnableOutboxProcessor()|};
                              }
                              """;

        return AnalyzerTest.VerifyDiagnosticAsync<ProcessorEnabledWithoutDispatcherAnalyzer>(
            source,
            DiagnosticDescriptors.ProcessorEnabledWithoutDispatcher,
            0,
            "Outbox module configuration",
            "Outbox",
            "UseInProcessDispatch");
    }

    /// <summary>
    ///     Verifies outbox processor enablement with UseInProcessDispatch in the same scope produces no diagnostic.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task OutboxProcessorWithUseInProcessDispatch_ProducesNoDiagnostic()
    {
        const string source = """
                              namespace LiteBus.Outbox.Abstractions;

                              public sealed class OutboxModuleBuilder
                              {
                                  public OutboxModuleBuilder EnableOutboxProcessor() => this;
                                  public OutboxModuleBuilder UseInProcessDispatch() => this;
                              }

                              public static class OutboxConfiguration
                              {
                                  public static void Configure(OutboxModuleBuilder outbox)
                                  {
                                      outbox.UseInProcessDispatch();
                                      outbox.EnableOutboxProcessor();
                                  }
                              }
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<ProcessorEnabledWithoutDispatcherAnalyzer>(source);
    }
}