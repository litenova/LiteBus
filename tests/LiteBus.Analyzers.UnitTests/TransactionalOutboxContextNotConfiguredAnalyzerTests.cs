using Microsoft.CodeAnalysis;

namespace LiteBus.Analyzers.UnitTests;

/// <summary>
///     Tests for the <see cref="TransactionalOutboxContextNotConfiguredAnalyzer" /> rule.
/// </summary>
public sealed class TransactionalOutboxContextNotConfiguredAnalyzerTests
{
    /// <summary>
    ///     Verifies that a transactional outbox injected for a configured context produces no diagnostic.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task OutboxConfiguredForInjectedContext_ProducesNoDiagnostic()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Outbox.Abstractions;
                              using LiteBus.Outbox.Storage.EntityFrameworkCore;

                              namespace LiteBus.Outbox.Storage.EntityFrameworkCore
                              {
                                  public sealed class EfCoreOutboxStorageModuleBuilder
                                  {
                                      public EfCoreOutboxStorageModuleBuilder UseDbContext<TContext>() => this;
                                  }
                              }

                              public sealed class OrdersDbContext
                              {
                              }

                              public sealed record CreateOrderCommand(int OrderId) : ICommand;

                              public sealed class CreateOrderCommandHandler : ICommandHandler<CreateOrderCommand>
                              {
                                  public CreateOrderCommandHandler(ITransactionalOutbox<OrdersDbContext> outbox)
                                  {
                                  }

                                  public Task HandleAsync(CreateOrderCommand command, CancellationToken cancellationToken = default)
                                      => Task.CompletedTask;
                              }

                              public static class OutboxStorageConfiguration
                              {
                                  public static void Configure(EfCoreOutboxStorageModuleBuilder builder)
                                  {
                                      builder.UseDbContext<OrdersDbContext>();
                                  }
                              }
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<TransactionalOutboxContextNotConfiguredAnalyzer>(source);
    }

    /// <summary>
    ///     Verifies that a transactional outbox injected for an unconfigured context produces LB1026.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task OutboxConfiguredForDifferentContext_ProducesDiagnostic()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Outbox.Abstractions;
                              using LiteBus.Outbox.Storage.EntityFrameworkCore;

                              namespace LiteBus.Outbox.Storage.EntityFrameworkCore
                              {
                                  public sealed class EfCoreOutboxStorageModuleBuilder
                                  {
                                      public EfCoreOutboxStorageModuleBuilder UseDbContext<TContext>() => this;
                                  }
                              }

                              public sealed class OrdersDbContext
                              {
                              }

                              public sealed class BillingDbContext
                              {
                              }

                              public sealed record CreateOrderCommand(int OrderId) : ICommand;

                              public sealed class CreateOrderCommandHandler : ICommandHandler<CreateOrderCommand>
                              {
                                  public CreateOrderCommandHandler(ITransactionalOutbox<BillingDbContext> {|#0:outbox|})
                                  {
                                  }

                                  public Task HandleAsync(CreateOrderCommand command, CancellationToken cancellationToken = default)
                                      => Task.CompletedTask;
                              }

                              public static class OutboxStorageConfiguration
                              {
                                  public static void Configure(EfCoreOutboxStorageModuleBuilder builder)
                                  {
                                      builder.UseDbContext<OrdersDbContext>();
                                  }
                              }
                              """;

        return AnalyzerTest.VerifyDiagnosticAsync<TransactionalOutboxContextNotConfiguredAnalyzer>(
            source,
            DiagnosticDescriptors.TransactionalOutboxContextNotConfigured,
            0,
            "CreateOrderCommandHandler",
            "BillingDbContext");
    }

    /// <summary>
    ///     Verifies that an injected transactional outbox produces no diagnostic when the compilation holds no storage call.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task NoUseDbContextCall_ProducesNoDiagnostic()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Outbox.Abstractions;

                              public sealed class OrdersDbContext
                              {
                              }

                              public sealed record CreateOrderCommand(int OrderId) : ICommand;

                              public sealed class CreateOrderCommandHandler : ICommandHandler<CreateOrderCommand>
                              {
                                  public CreateOrderCommandHandler(ITransactionalOutbox<OrdersDbContext> outbox)
                                  {
                                  }

                                  public Task HandleAsync(CreateOrderCommand command, CancellationToken cancellationToken = default)
                                      => Task.CompletedTask;
                              }
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<TransactionalOutboxContextNotConfiguredAnalyzer>(source);
    }

    /// <summary>
    ///     Verifies that a context configured through the inbox storage builder satisfies the rule.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task InboxBuilderConfiguresContext_ProducesNoDiagnostic()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Inbox.Storage.EntityFrameworkCore;
                              using LiteBus.Outbox.Abstractions;

                              namespace LiteBus.Inbox.Storage.EntityFrameworkCore
                              {
                                  public sealed class EfCoreInboxStorageModuleBuilder
                                  {
                                      public EfCoreInboxStorageModuleBuilder UseDbContext<TContext>() => this;
                                  }
                              }

                              public sealed class OrdersDbContext
                              {
                              }

                              public sealed record CreateOrderCommand(int OrderId) : ICommand;

                              public sealed class CreateOrderCommandHandler : ICommandHandler<CreateOrderCommand>
                              {
                                  public CreateOrderCommandHandler(ITransactionalOutbox<OrdersDbContext> outbox)
                                  {
                                  }

                                  public Task HandleAsync(CreateOrderCommand command, CancellationToken cancellationToken = default)
                                      => Task.CompletedTask;
                              }

                              public static class InboxStorageConfiguration
                              {
                                  public static void Configure(EfCoreInboxStorageModuleBuilder builder)
                                  {
                                      builder.UseDbContext<OrdersDbContext>();
                                  }
                              }
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<TransactionalOutboxContextNotConfiguredAnalyzer>(source);
    }

    /// <summary>
    ///     Verifies that a same-named <c>UseDbContext</c> call on another type does not configure the context.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task UseDbContextOnOtherType_ProducesNoDiagnostic()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Outbox.Abstractions;

                              namespace App.Storage
                              {
                                  public sealed class AppStorageBuilder
                                  {
                                      public AppStorageBuilder UseDbContext<TContext>() => this;
                                  }
                              }

                              public sealed class OrdersDbContext
                              {
                              }

                              public sealed record CreateOrderCommand(int OrderId) : ICommand;

                              public sealed class CreateOrderCommandHandler : ICommandHandler<CreateOrderCommand>
                              {
                                  public CreateOrderCommandHandler(ITransactionalOutbox<OrdersDbContext> outbox)
                                  {
                                  }

                                  public Task HandleAsync(CreateOrderCommand command, CancellationToken cancellationToken = default)
                                      => Task.CompletedTask;
                              }

                              public static class AppStorageConfiguration
                              {
                                  public static void Configure(App.Storage.AppStorageBuilder builder)
                                  {
                                      builder.UseDbContext<OrdersDbContext>();
                                  }
                              }
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<TransactionalOutboxContextNotConfiguredAnalyzer>(source);
    }

    /// <summary>
    ///     Verifies that two unconfigured contexts produce one LB1026 diagnostic each.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task MultipleUnconfiguredContexts_ProduceDiagnostics()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Outbox.Abstractions;
                              using LiteBus.Outbox.Storage.EntityFrameworkCore;

                              namespace LiteBus.Outbox.Storage.EntityFrameworkCore
                              {
                                  public sealed class EfCoreOutboxStorageModuleBuilder
                                  {
                                      public EfCoreOutboxStorageModuleBuilder UseDbContext<TContext>() => this;
                                  }
                              }

                              public sealed class OrdersDbContext
                              {
                              }

                              public sealed class BillingDbContext
                              {
                              }

                              public sealed class ShippingDbContext
                              {
                              }

                              public sealed record CreateBillingCommand(int OrderId) : ICommand;

                              public sealed class CreateBillingCommandHandler : ICommandHandler<CreateBillingCommand>
                              {
                                  public CreateBillingCommandHandler(ITransactionalOutbox<BillingDbContext> {|#0:outbox|})
                                  {
                                  }

                                  public Task HandleAsync(CreateBillingCommand command, CancellationToken cancellationToken = default)
                                      => Task.CompletedTask;
                              }

                              public sealed record CreateShippingCommand(int OrderId) : ICommand;

                              public sealed class CreateShippingCommandHandler : ICommandHandler<CreateShippingCommand>
                              {
                                  public CreateShippingCommandHandler(ITransactionalOutbox<ShippingDbContext> {|#1:outbox|})
                                  {
                                  }

                                  public Task HandleAsync(CreateShippingCommand command, CancellationToken cancellationToken = default)
                                      => Task.CompletedTask;
                              }

                              public static class OutboxStorageConfiguration
                              {
                                  public static void Configure(EfCoreOutboxStorageModuleBuilder builder)
                                  {
                                      builder.UseDbContext<OrdersDbContext>();
                                  }
                              }
                              """;

        return AnalyzerTest.VerifyDiagnosticsAsync<TransactionalOutboxContextNotConfiguredAnalyzer>(
            source,
            (DiagnosticDescriptors.TransactionalOutboxContextNotConfigured, 0, ["CreateBillingCommandHandler", "BillingDbContext"]),
            (DiagnosticDescriptors.TransactionalOutboxContextNotConfigured, 1, ["CreateShippingCommandHandler", "ShippingDbContext"]));
    }

    /// <summary>
    ///     Verifies that a class outside command handling that injects an unconfigured context produces LB1026.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task NonHandlerClassInjectingUnconfiguredContext_ProducesDiagnostic()
    {
        const string source = """
                              using LiteBus.Outbox.Abstractions;
                              using LiteBus.Outbox.Storage.EntityFrameworkCore;

                              namespace LiteBus.Outbox.Storage.EntityFrameworkCore
                              {
                                  public sealed class EfCoreOutboxStorageModuleBuilder
                                  {
                                      public EfCoreOutboxStorageModuleBuilder UseDbContext<TContext>() => this;
                                  }
                              }

                              public sealed class OrdersDbContext
                              {
                              }

                              public sealed class BillingDbContext
                              {
                              }

                              public sealed class OrderService
                              {
                                  public OrderService(ITransactionalOutbox<BillingDbContext> {|#0:outbox|})
                                  {
                                  }
                              }

                              public static class OutboxStorageConfiguration
                              {
                                  public static void Configure(EfCoreOutboxStorageModuleBuilder builder)
                                  {
                                      builder.UseDbContext<OrdersDbContext>();
                                  }
                              }
                              """;

        return AnalyzerTest.VerifyDiagnosticAsync<TransactionalOutboxContextNotConfiguredAnalyzer>(
            source,
            DiagnosticDescriptors.TransactionalOutboxContextNotConfigured,
            0,
            "OrderService",
            "BillingDbContext");
    }

    /// <summary>
    ///     Verifies that the rule is a warning in <c>LiteBus.Outbox</c> that stays on by default.
    /// </summary>
    [Fact]
    public void Descriptor_IsWarningInOutboxCategoryAndEnabledByDefault()
    {
        Assert.Equal("LiteBus.Outbox", DiagnosticDescriptors.TransactionalOutboxContextNotConfigured.Category);
        Assert.Equal(DiagnosticSeverity.Warning, DiagnosticDescriptors.TransactionalOutboxContextNotConfigured.DefaultSeverity);
        Assert.True(DiagnosticDescriptors.TransactionalOutboxContextNotConfigured.IsEnabledByDefault);
    }
}
