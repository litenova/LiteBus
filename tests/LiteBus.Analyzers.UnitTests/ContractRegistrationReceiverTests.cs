namespace LiteBus.Analyzers.UnitTests;

/// <summary>
///     Tests that durable contract registration is matched by receiver type rather than by member name.
/// </summary>
public sealed class ContractRegistrationReceiverTests
{
    /// <summary>
    ///     Verifies that assembly scanning through <c>CommandModuleBuilder</c> does not satisfy explicit registration.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task AttributedCommandScannedOnlyByCommandModuleBuilder_ProducesDiagnostic()
    {
        const string source = """
                              using System.Reflection;
                              using LiteBus.Commands;
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Messaging.Abstractions;

                              [MessageContract("payments.process-payment", 1)]
                              public sealed record {|#0:ProcessPaymentCommand|}(int PaymentId) : ICommand;

                              public static class ModuleConfiguration
                              {
                                  public static void Configure(CommandModuleBuilder commands)
                                  {
                                      commands.RegisterFromAssembly(typeof(ProcessPaymentCommand).Assembly);
                                  }
                              }

                              namespace LiteBus.Commands
                              {
                                  public sealed class CommandModuleBuilder
                                  {
                                      public CommandModuleBuilder Register<T>()
                                      {
                                          return this;
                                      }

                                      public CommandModuleBuilder RegisterFromAssembly(Assembly assembly)
                                      {
                                          return this;
                                      }
                                  }
                              }
                              """;

        return AnalyzerTest.VerifyDiagnosticAsync<ExplicitMessageContractRegistrationAnalyzer>(
            source,
            DiagnosticDescriptors.ExplicitMessageContractRegistration,
            0,
            "ProcessPaymentCommand");
    }

    /// <summary>
    ///     Verifies that assembly scanning through <c>MessageModuleBuilder</c> satisfies explicit registration.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task AttributedCommandScannedByMessageModuleBuilder_ProducesNoDiagnostic()
    {
        const string source = """
                              using System.Reflection;
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Messaging;
                              using LiteBus.Messaging.Abstractions;

                              [MessageContract("payments.process-payment", 1)]
                              public sealed record ProcessPaymentCommand(int PaymentId) : ICommand;

                              public static class ModuleConfiguration
                              {
                                  public static void Configure(MessageModuleBuilder messaging)
                                  {
                                      messaging.RegisterFromAssembly(typeof(ProcessPaymentCommand).Assembly);
                                  }
                              }

                              namespace LiteBus.Messaging
                              {
                                  public sealed class MessageModuleBuilder
                                  {
                                      public MessageModuleBuilder Register<T>()
                                      {
                                          return this;
                                      }

                                      public MessageModuleBuilder RegisterFromAssembly(Assembly assembly)
                                      {
                                          return this;
                                      }
                                  }
                              }
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<ExplicitMessageContractRegistrationAnalyzer>(source);
    }

    /// <summary>
    ///     Verifies that <c>Register</c> on the real contract builder satisfies explicit registration.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task AttributedCommandRegisteredThroughContractBuilder_ProducesNoDiagnostic()
    {
        const string source = """
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Messaging.Abstractions;

                              [MessageContract("payments.process-payment", 1)]
                              public sealed record ProcessPaymentCommand(int PaymentId) : ICommand;

                              public static class ModuleConfiguration
                              {
                                  public static void Configure(MessageContractBuilder contracts)
                                  {
                                      contracts.Register<ProcessPaymentCommand>("payments.process-payment", 1);
                                  }
                              }
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<ExplicitMessageContractRegistrationAnalyzer>(source);
    }

    /// <summary>
    ///     Verifies that <c>Register</c> on a message builder does not satisfy durable contract registration.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task HandledCommandRegisteredOnlyThroughMessageModuleBuilder_ProducesDiagnostic()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Messaging;

                              public sealed record ProcessPaymentCommand(int PaymentId) : ICommand;

                              public sealed class {|#0:ProcessPaymentCommandHandler|} : ICommandHandler<ProcessPaymentCommand>
                              {
                                  public Task HandleAsync(ProcessPaymentCommand command, CancellationToken cancellationToken = default)
                                      => Task.CompletedTask;
                              }

                              public static class ModuleConfiguration
                              {
                                  public static void Configure(MessageModuleBuilder messaging)
                                  {
                                      messaging.Register<ProcessPaymentCommand>();
                                  }
                              }

                              namespace LiteBus.Messaging
                              {
                                  public sealed class MessageModuleBuilder
                                  {
                                      public MessageModuleBuilder Register<T>()
                                      {
                                          return this;
                                      }

                                      public MessageModuleBuilder RegisterFromAssembly(System.Reflection.Assembly assembly)
                                      {
                                          return this;
                                      }
                                  }
                              }
                              """;

        return AnalyzerTest.VerifyDiagnosticAsync<MissingMessageContractRegistrationAnalyzer>(
            source,
            DiagnosticDescriptors.MissingMessageContractRegistration,
            0,
            "ProcessPaymentCommand",
            "ProcessPaymentCommandHandler",
            "ProcessPaymentCommand");
    }

    /// <summary>
    ///     Verifies that assembly scanning through a message builder satisfies durable contract registration.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task HandledCommandScannedByMessageModuleBuilder_ProducesNoDiagnostic()
    {
        const string source = """
                              using System.Reflection;
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Messaging;

                              public sealed record ProcessPaymentCommand(int PaymentId) : ICommand;

                              public sealed class ProcessPaymentCommandHandler : ICommandHandler<ProcessPaymentCommand>
                              {
                                  public Task HandleAsync(ProcessPaymentCommand command, CancellationToken cancellationToken = default)
                                      => Task.CompletedTask;
                              }

                              public static class ModuleConfiguration
                              {
                                  public static void Configure(MessageModuleBuilder messaging)
                                  {
                                      messaging.RegisterFromAssembly(typeof(ProcessPaymentCommand).Assembly);
                                  }
                              }

                              namespace LiteBus.Messaging
                              {
                                  public sealed class MessageModuleBuilder
                                  {
                                      public MessageModuleBuilder Register<T>()
                                      {
                                          return this;
                                      }

                                      public MessageModuleBuilder RegisterFromAssembly(Assembly assembly)
                                      {
                                          return this;
                                      }
                                  }
                              }
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<MissingMessageContractRegistrationAnalyzer>(source);
    }

    /// <summary>
    ///     Verifies that <c>Register</c> on a contract writer satisfies durable contract registration.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task HandledCommandRegisteredThroughContractWriter_ProducesNoDiagnostic()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Messaging.Abstractions;

                              public sealed record ProcessPaymentCommand(int PaymentId) : ICommand;

                              public sealed class ProcessPaymentCommandHandler : ICommandHandler<ProcessPaymentCommand>
                              {
                                  public Task HandleAsync(ProcessPaymentCommand command, CancellationToken cancellationToken = default)
                                      => Task.CompletedTask;
                              }

                              public static class ModuleConfiguration
                              {
                                  public static void Configure(MessageContractBuilder contracts)
                                  {
                                      contracts.Register<ProcessPaymentCommand>("payments.process-payment", 1);
                                  }
                              }
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<MissingMessageContractRegistrationAnalyzer>(source);
    }
}
