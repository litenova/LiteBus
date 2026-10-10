namespace LiteBus.Analyzers.UnitTests;

/// <summary>
///     Tests for the <see cref="UntypedShortcutOnResultMessageAnalyzer" /> rule.
/// </summary>
public sealed class UntypedShortcutOnResultMessageAnalyzerTests
{
    /// <summary>
    ///     Verifies that an untyped shortcut over a command that produces no result produces no diagnostic.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task UntypedShortcutOnVoidCommand_ProducesNoDiagnostic()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Messaging.Abstractions;

                              public sealed record RefundOrderCommand : ICommand;

                              public sealed class SkipRefundedOrder : ICommandShortcut<RefundOrderCommand>
                              {
                                  public Task<Shortcut> TryAnswerAsync(
                                      RefundOrderCommand message,
                                      CancellationToken cancellationToken = default)
                                      => Task.FromResult(Shortcut.None);
                              }
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<UntypedShortcutOnResultMessageAnalyzer>(source);
    }

    /// <summary>
    ///     Verifies that the typed shortcut over a command that produces a result produces no diagnostic.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task TypedShortcutOnResultCommand_ProducesNoDiagnostic()
    {
        const string source = """
                              using System;
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Messaging.Abstractions;

                              public sealed record CreateProductCommand : ICommand<Guid>;

                              public sealed class ServeCreatedProduct : ICommandShortcut<CreateProductCommand, Guid>
                              {
                                  public Task<Shortcut<Guid>> TryAnswerAsync(
                                      CreateProductCommand message,
                                      CancellationToken cancellationToken = default)
                                      => Task.FromResult(Shortcut<Guid>.None);
                              }
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<UntypedShortcutOnResultMessageAnalyzer>(source);
    }

    /// <summary>
    ///     Verifies that an untyped guard over a command that produces a result produces no diagnostic.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    /// <remarks>
    ///     A refusal never owes the caller the value the handler would have produced, so the untyped guard is the
    ///     correct contract here. Reporting it would push authors toward a typed contract they do not need.
    /// </remarks>
    [Fact]
    public Task UntypedGuardOnResultCommand_ProducesNoDiagnostic()
    {
        const string source = """
                              using System;
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Messaging.Abstractions;

                              public sealed record CreateProductCommand : ICommand<Guid>;

                              public sealed class RejectDuplicateProduct : ICommandGuard<CreateProductCommand>
                              {
                                  public Task<Verdict> DecideAsync(
                                      CreateProductCommand message,
                                      CancellationToken cancellationToken = default)
                                      => Task.FromResult(Verdict.Allow);
                              }
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<UntypedShortcutOnResultMessageAnalyzer>(source);
    }

    /// <summary>
    ///     Verifies that an event shortcut produces no diagnostic, because an event produces no result.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task EventShortcut_ProducesNoDiagnostic()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Events.Abstractions;
                              using LiteBus.Messaging.Abstractions;

                              public sealed record OrderPlaced : IEvent;

                              public sealed class SkipHandledOrderPlaced : IEventShortcut<OrderPlaced>
                              {
                                  public Task<Shortcut> TryAnswerAsync(
                                      OrderPlaced message,
                                      CancellationToken cancellationToken = default)
                                      => Task.FromResult(Shortcut.None);
                              }
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<UntypedShortcutOnResultMessageAnalyzer>(source);
    }

    /// <summary>
    ///     Verifies that an open generic shortcut produces no diagnostic, because its message type is unknown.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task OpenGenericShortcut_ProducesNoDiagnostic()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Messaging.Abstractions;

                              public sealed class CommandShortcut<TCommand> : ICommandShortcut<TCommand>
                                  where TCommand : ICommand
                              {
                                  public Task<Shortcut> TryAnswerAsync(
                                      TCommand message,
                                      CancellationToken cancellationToken = default)
                                      => Task.FromResult(Shortcut.None);
                              }
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<UntypedShortcutOnResultMessageAnalyzer>(source);
    }

    /// <summary>
    ///     Verifies that an untyped shortcut over a command that produces a result produces LB1019.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task UntypedShortcutOnResultCommand_ProducesDiagnostic()
    {
        const string source = """
                              using System;
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Messaging.Abstractions;

                              public sealed record CreateProductCommand : ICommand<Guid>;

                              public sealed class {|#0:SkipCreatedProduct|} : ICommandShortcut<CreateProductCommand>
                              {
                                  public Task<Shortcut> TryAnswerAsync(
                                      CreateProductCommand message,
                                      CancellationToken cancellationToken = default)
                                      => Task.FromResult(Shortcut.Answer("already created"));
                              }
                              """;

        return AnalyzerTest.VerifyDiagnosticAsync<UntypedShortcutOnResultMessageAnalyzer>(
            source,
            DiagnosticDescriptors.UntypedShortcutOnResultMessage,
            0,
            "SkipCreatedProduct",
            "CreateProductCommand",
            "Guid",
            "ICommandShortcut");
    }

    /// <summary>
    ///     Verifies that an untyped shortcut over a query produces LB1019 naming the query shortcut contract.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task UntypedShortcutOnQuery_ProducesDiagnostic()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Messaging.Abstractions;
                              using LiteBus.Queries.Abstractions;

                              public sealed record GetProductQuery : IQuery<string>;

                              public sealed class {|#0:ProductCacheLookup|} : IMessageShortcut<GetProductQuery>
                              {
                                  public Task<Shortcut> TryAnswerAsync(
                                      GetProductQuery message,
                                      CancellationToken cancellationToken = default)
                                      => Task.FromResult(Shortcut.None);
                              }
                              """;

        return AnalyzerTest.VerifyDiagnosticAsync<UntypedShortcutOnResultMessageAnalyzer>(
            source,
            DiagnosticDescriptors.UntypedShortcutOnResultMessage,
            0,
            "ProductCacheLookup",
            "GetProductQuery",
            "string",
            "IQueryShortcut");
    }

    /// <summary>
    ///     Verifies that an untyped shortcut over a stream query produces LB1019 naming the stream contract.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task UntypedShortcutOnStreamQuery_ProducesDiagnostic()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Messaging.Abstractions;
                              using LiteBus.Queries.Abstractions;

                              public sealed record StreamProductsQuery : IStreamQuery<string>;

                              public sealed class {|#0:StreamProductsShortcut|} : IMessageShortcut<StreamProductsQuery>
                              {
                                  public Task<Shortcut> TryAnswerAsync(
                                      StreamProductsQuery message,
                                      CancellationToken cancellationToken = default)
                                      => Task.FromResult(Shortcut.None);
                              }
                              """;

        return AnalyzerTest.VerifyDiagnosticAsync<UntypedShortcutOnResultMessageAnalyzer>(
            source,
            DiagnosticDescriptors.UntypedShortcutOnResultMessage,
            0,
            "StreamProductsShortcut",
            "StreamProductsQuery",
            "string",
            "IStreamQueryShortcut");
    }
}
