using LiteBus.Commands.Abstractions;
using LiteBus.Events.Abstractions;
using LiteBus.Inbox.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox.Abstractions;
using LiteBus.Queries.Abstractions;
using LiteBus.Transport.Abstractions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace LiteBus.Analyzers.UnitTests;

/// <summary>
///     Tests for cross-assembly handler coverage in
///     <see cref="MissingCommandHandlerAnalyzer" /> and
///     <see cref="MissingQueryHandlerAnalyzer" />.
/// </summary>
public sealed class CrossAssemblyHandlerCoverageTests
{
    /// <summary>
    ///     Verifies an unhandled command in a referenced assembly that names the analyzed compilation produces LB1008 at the first location.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task UnhandledCommandInReferencedAssemblyNamingCurrentCompilation_ProducesDiagnostic()
    {
        const string contractsSource = """
                                       using LiteBus.Commands.Abstractions;
                                       using LiteBus.Messaging.Abstractions;

                                       [assembly: MessageContractsRegisteredIn("TestProject")]

                                       public sealed record ProcessPaymentCommand(int PaymentId) : ICommand;
                                       """;

        const string source = """
                              {|#0:public|} sealed class Placeholder
                              {
                              }
                              """;

        var commandsReference = MetadataReference.CreateFromFile(typeof(ICommand).Assembly.Location);
        var messagingReference = MetadataReference.CreateFromFile(typeof(MessageContractAttribute).Assembly.Location);
        var contractsReference = AnalyzerTest.CompileToMetadataReference(
            "ContractsAssembly",
            contractsSource,
            commandsReference,
            messagingReference);

        return VerifyWithReferencesAsync<MissingCommandHandlerAnalyzer>(
            source,
            [contractsReference],
            (DiagnosticDescriptors.MissingCommandHandler, 0, ["ProcessPaymentCommand"]));
    }

    /// <summary>
    ///     Verifies a command in a referenced assembly that names the analyzed compilation with a handler produces no diagnostic.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task HandledCommandInReferencedAssemblyNamingCurrentCompilation_ProducesNoDiagnostic()
    {
        const string contractsSource = """
                                       using LiteBus.Commands.Abstractions;
                                       using LiteBus.Messaging.Abstractions;

                                       [assembly: MessageContractsRegisteredIn("TestProject")]

                                       public sealed record ProcessPaymentCommand(int PaymentId) : ICommand;
                                       """;

        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;

                              public sealed class ProcessPaymentCommandHandler : ICommandHandler<ProcessPaymentCommand>
                              {
                                  public Task HandleAsync(ProcessPaymentCommand command, CancellationToken cancellationToken = default)
                                      => Task.CompletedTask;
                              }
                              """;

        var commandsReference = MetadataReference.CreateFromFile(typeof(ICommand).Assembly.Location);
        var messagingReference = MetadataReference.CreateFromFile(typeof(MessageContractAttribute).Assembly.Location);
        var contractsReference = AnalyzerTest.CompileToMetadataReference(
            "ContractsAssembly",
            contractsSource,
            commandsReference,
            messagingReference);

        return AnalyzerTest.VerifyNoDiagnosticsWithReferencesAsync<MissingCommandHandlerAnalyzer>(
            source,
            contractsReference);
    }

    /// <summary>
    ///     Verifies an unhandled command in a referenced assembly that names another assembly produces no diagnostic.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task UnhandledCommandInReferencedAssemblyNamingAnotherAssembly_ProducesNoDiagnostic()
    {
        const string contractsSource = """
                                       using LiteBus.Commands.Abstractions;
                                       using LiteBus.Messaging.Abstractions;

                                       [assembly: MessageContractsRegisteredIn("LedgerModule")]

                                       public sealed record ProcessPaymentCommand(int PaymentId) : ICommand;
                                       """;

        const string source = """
                              public sealed class Placeholder
                              {
                              }
                              """;

        var commandsReference = MetadataReference.CreateFromFile(typeof(ICommand).Assembly.Location);
        var messagingReference = MetadataReference.CreateFromFile(typeof(MessageContractAttribute).Assembly.Location);
        var contractsReference = AnalyzerTest.CompileToMetadataReference(
            "ContractsAssembly",
            contractsSource,
            commandsReference,
            messagingReference);

        return AnalyzerTest.VerifyNoDiagnosticsWithReferencesAsync<MissingCommandHandlerAnalyzer>(
            source,
            contractsReference);
    }

    /// <summary>
    ///     Verifies an unhandled command in a referenced assembly without a registering assembly attribute produces no diagnostic.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task UnhandledCommandInReferencedAssemblyWithoutAttribute_ProducesNoDiagnostic()
    {
        const string contractsSource = """
                                       using LiteBus.Commands.Abstractions;

                                       public sealed record ProcessPaymentCommand(int PaymentId) : ICommand;
                                       """;

        const string source = """
                              public sealed class Placeholder
                              {
                              }
                              """;

        var commandsReference = MetadataReference.CreateFromFile(typeof(ICommand).Assembly.Location);
        var messagingReference = MetadataReference.CreateFromFile(typeof(MessageContractAttribute).Assembly.Location);
        var contractsReference = AnalyzerTest.CompileToMetadataReference(
            "ContractsAssembly",
            contractsSource,
            commandsReference,
            messagingReference);

        return AnalyzerTest.VerifyNoDiagnosticsWithReferencesAsync<MissingCommandHandlerAnalyzer>(
            source,
            contractsReference);
    }

    /// <summary>
    ///     Verifies two unhandled commands in a referenced assembly that names the analyzed compilation produce two LB1008 diagnostics in metadata name order.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task TwoUnhandledCommandsInReferencedAssemblyNamingCurrentCompilation_ProduceTwoDiagnostics()
    {
        const string contractsSource = """
                                       using LiteBus.Commands.Abstractions;
                                       using LiteBus.Messaging.Abstractions;

                                       [assembly: MessageContractsRegisteredIn("TestProject")]

                                       public sealed record ProcessPaymentCommand(int PaymentId) : ICommand;

                                       public sealed record CancelPaymentCommand(int PaymentId) : ICommand;
                                       """;

        const string source = """
                              {|#0:public|} sealed class Placeholder
                              {
                              }
                              """;

        var commandsReference = MetadataReference.CreateFromFile(typeof(ICommand).Assembly.Location);
        var messagingReference = MetadataReference.CreateFromFile(typeof(MessageContractAttribute).Assembly.Location);
        var contractsReference = AnalyzerTest.CompileToMetadataReference(
            "ContractsAssembly",
            contractsSource,
            commandsReference,
            messagingReference);

        return VerifyWithReferencesAsync<MissingCommandHandlerAnalyzer>(
            source,
            [contractsReference],
            (DiagnosticDescriptors.MissingCommandHandler, 0, ["CancelPaymentCommand"]),
            (DiagnosticDescriptors.MissingCommandHandler, 0, ["ProcessPaymentCommand"]));
    }

    /// <summary>
    ///     Verifies a referenced command covered by a handler for its base command type produces no diagnostic.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task ReferencedCommandCoveredByBaseHandler_ProducesNoDiagnostic()
    {
        const string contractsSource = """
                                       using LiteBus.Commands.Abstractions;
                                       using LiteBus.Messaging.Abstractions;

                                       [assembly: MessageContractsRegisteredIn("TestProject")]

                                       public abstract record BaseCommand : ICommand;

                                       public sealed record SpecializedCommand : BaseCommand;
                                       """;

        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;

                              public sealed class BaseCommandHandler : ICommandHandler<BaseCommand>
                              {
                                  public Task HandleAsync(BaseCommand command, CancellationToken cancellationToken = default)
                                      => Task.CompletedTask;
                              }
                              """;

        var commandsReference = MetadataReference.CreateFromFile(typeof(ICommand).Assembly.Location);
        var messagingReference = MetadataReference.CreateFromFile(typeof(MessageContractAttribute).Assembly.Location);
        var contractsReference = AnalyzerTest.CompileToMetadataReference(
            "ContractsAssembly",
            contractsSource,
            commandsReference,
            messagingReference);

        return AnalyzerTest.VerifyNoDiagnosticsWithReferencesAsync<MissingCommandHandlerAnalyzer>(
            source,
            contractsReference);
    }

    /// <summary>
    ///     Verifies a referenced command covered by an open generic handler produces no diagnostic.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task ReferencedCommandCoveredByOpenGenericHandler_ProducesNoDiagnostic()
    {
        const string contractsSource = """
                                       using LiteBus.Commands.Abstractions;
                                       using LiteBus.Messaging.Abstractions;

                                       [assembly: MessageContractsRegisteredIn("TestProject")]

                                       public sealed record ProcessPaymentCommand(int PaymentId) : ICommand;
                                       """;

        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;

                              public sealed class GenericCommandHandler<TCommand> : ICommandHandler<TCommand>
                                  where TCommand : ICommand
                              {
                                  public Task HandleAsync(TCommand command, CancellationToken cancellationToken = default)
                                      => Task.CompletedTask;
                              }
                              """;

        var commandsReference = MetadataReference.CreateFromFile(typeof(ICommand).Assembly.Location);
        var messagingReference = MetadataReference.CreateFromFile(typeof(MessageContractAttribute).Assembly.Location);
        var contractsReference = AnalyzerTest.CompileToMetadataReference(
            "ContractsAssembly",
            contractsSource,
            commandsReference,
            messagingReference);

        return AnalyzerTest.VerifyNoDiagnosticsWithReferencesAsync<MissingCommandHandlerAnalyzer>(
            source,
            contractsReference);
    }

    /// <summary>
    ///     Verifies an unhandled query in a referenced assembly that names the analyzed compilation produces LB1009 at the first location.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task UnhandledQueryInReferencedAssemblyNamingCurrentCompilation_ProducesDiagnostic()
    {
        const string contractsSource = """
                                       using LiteBus.Messaging.Abstractions;
                                       using LiteBus.Queries.Abstractions;

                                       [assembly: MessageContractsRegisteredIn("TestProject")]

                                       public sealed record GetUserQuery(int UserId) : IQuery<string>;
                                       """;

        const string source = """
                              {|#0:public|} sealed class Placeholder
                              {
                              }
                              """;

        var queriesReference = MetadataReference.CreateFromFile(typeof(IQuery<>).Assembly.Location);
        var messagingReference = MetadataReference.CreateFromFile(typeof(MessageContractAttribute).Assembly.Location);
        var contractsReference = AnalyzerTest.CompileToMetadataReference(
            "ContractsAssembly",
            contractsSource,
            queriesReference,
            messagingReference);

        return VerifyWithReferencesAsync<MissingQueryHandlerAnalyzer>(
            source,
            [contractsReference],
            (DiagnosticDescriptors.MissingQueryHandler, 0, ["GetUserQuery"]));
    }

    /// <summary>
    ///     Verifies a query in a referenced assembly that names the analyzed compilation with a handler produces no diagnostic.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task HandledQueryInReferencedAssemblyNamingCurrentCompilation_ProducesNoDiagnostic()
    {
        const string contractsSource = """
                                       using LiteBus.Messaging.Abstractions;
                                       using LiteBus.Queries.Abstractions;

                                       [assembly: MessageContractsRegisteredIn("TestProject")]

                                       public sealed record GetUserQuery(int UserId) : IQuery<string>;
                                       """;

        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Queries.Abstractions;

                              public sealed class GetUserQueryHandler : IQueryHandler<GetUserQuery, string>
                              {
                                  public Task<string> HandleAsync(GetUserQuery query, CancellationToken cancellationToken = default)
                                      => Task.FromResult("Ada");
                              }
                              """;

        var queriesReference = MetadataReference.CreateFromFile(typeof(IQuery<>).Assembly.Location);
        var messagingReference = MetadataReference.CreateFromFile(typeof(MessageContractAttribute).Assembly.Location);
        var contractsReference = AnalyzerTest.CompileToMetadataReference(
            "ContractsAssembly",
            contractsSource,
            queriesReference,
            messagingReference);

        return AnalyzerTest.VerifyNoDiagnosticsWithReferencesAsync<MissingQueryHandlerAnalyzer>(
            source,
            contractsReference);
    }

    /// <summary>
    ///     Verifies an unhandled stream query in a referenced assembly that names the analyzed compilation produces one LB1009 diagnostic.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task UnhandledStreamQueryInReferencedAssemblyNamingCurrentCompilation_ProducesDiagnostic()
    {
        const string contractsSource = """
                                       using LiteBus.Messaging.Abstractions;
                                       using LiteBus.Queries.Abstractions;

                                       [assembly: MessageContractsRegisteredIn("TestProject")]

                                       public sealed record StreamUsersQuery(int PageSize) : IStreamQuery<string>;
                                       """;

        const string source = """
                              {|#0:public|} sealed class Placeholder
                              {
                              }
                              """;

        var queriesReference = MetadataReference.CreateFromFile(typeof(IQuery<>).Assembly.Location);
        var messagingReference = MetadataReference.CreateFromFile(typeof(MessageContractAttribute).Assembly.Location);
        var contractsReference = AnalyzerTest.CompileToMetadataReference(
            "ContractsAssembly",
            contractsSource,
            queriesReference,
            messagingReference);

        return VerifyWithReferencesAsync<MissingQueryHandlerAnalyzer>(
            source,
            [contractsReference],
            (DiagnosticDescriptors.MissingQueryHandler, 0, ["StreamUsersQuery"]));
    }

    /// <summary>
    ///     Verifies a stream query in a referenced assembly that names the analyzed compilation with a handler produces no diagnostic.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task HandledStreamQueryInReferencedAssemblyNamingCurrentCompilation_ProducesNoDiagnostic()
    {
        const string contractsSource = """
                                       using LiteBus.Messaging.Abstractions;
                                       using LiteBus.Queries.Abstractions;

                                       [assembly: MessageContractsRegisteredIn("TestProject")]

                                       public sealed record StreamUsersQuery(int PageSize) : IStreamQuery<string>;
                                       """;

        const string source = """
                              using System.Collections.Generic;
                              using System.Threading;
                              using LiteBus.Queries.Abstractions;

                              public sealed class StreamUsersQueryHandler : IStreamQueryHandler<StreamUsersQuery, string>
                              {
                                  public IAsyncEnumerable<string> StreamAsync(StreamUsersQuery query, CancellationToken cancellationToken = default)
                                      => Empty();

                                  private static async IAsyncEnumerable<string> Empty()
                                  {
                                      yield break;
                                  }
                              }
                              """;

        var queriesReference = MetadataReference.CreateFromFile(typeof(IQuery<>).Assembly.Location);
        var messagingReference = MetadataReference.CreateFromFile(typeof(MessageContractAttribute).Assembly.Location);
        var contractsReference = AnalyzerTest.CompileToMetadataReference(
            "ContractsAssembly",
            contractsSource,
            queriesReference,
            messagingReference);

        return AnalyzerTest.VerifyNoDiagnosticsWithReferencesAsync<MissingQueryHandlerAnalyzer>(
            source,
            contractsReference);
    }

    /// <summary>
    ///     Verifies an unhandled query in a referenced assembly that names another assembly produces no diagnostic.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task UnhandledQueryInReferencedAssemblyNamingAnotherAssembly_ProducesNoDiagnostic()
    {
        const string contractsSource = """
                                       using LiteBus.Messaging.Abstractions;
                                       using LiteBus.Queries.Abstractions;

                                       [assembly: MessageContractsRegisteredIn("LedgerModule")]

                                       public sealed record GetUserQuery(int UserId) : IQuery<string>;
                                       """;

        const string source = """
                              public sealed class Placeholder
                              {
                              }
                              """;

        var queriesReference = MetadataReference.CreateFromFile(typeof(IQuery<>).Assembly.Location);
        var messagingReference = MetadataReference.CreateFromFile(typeof(MessageContractAttribute).Assembly.Location);
        var contractsReference = AnalyzerTest.CompileToMetadataReference(
            "ContractsAssembly",
            contractsSource,
            queriesReference,
            messagingReference);

        return AnalyzerTest.VerifyNoDiagnosticsWithReferencesAsync<MissingQueryHandlerAnalyzer>(
            source,
            contractsReference);
    }

    /// <summary>
    ///     Verifies analyzer diagnostics for source compiled with additional metadata references.
    /// </summary>
    /// <typeparam name="TAnalyzer">The analyzer type under test.</typeparam>
    /// <param name="source">The source under test.</param>
    /// <param name="references">Additional metadata references for the compilation.</param>
    /// <param name="expectedDiagnostics">The expected diagnostics, empty when the compilation must be clean.</param>
    /// <returns>A task that completes when verification finishes.</returns>
    private static Task VerifyWithReferencesAsync<TAnalyzer>(
        string source,
        IReadOnlyList<MetadataReference> references,
        params (DiagnosticDescriptor Descriptor, int? MarkupLocation, object[] Arguments)[] expectedDiagnostics)
        where TAnalyzer : DiagnosticAnalyzer, new()
    {
        var test = new CSharpAnalyzerTest<TAnalyzer, DefaultVerifier>
        {
            ReferenceAssemblies = AnalyzerTest.Net10ReferenceAssemblies
        };

        test.TestState.Sources.Add(("/0/Test0.cs", source));

        test.TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(
            typeof(ICommand).Assembly.Location));

        test.TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(
            typeof(IEvent).Assembly.Location));

        test.TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(
            typeof(IQuery<>).Assembly.Location));

        test.TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(
            typeof(IInbox).Assembly.Location));

        test.TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(
            typeof(IOutbox).Assembly.Location));

        test.TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(
            typeof(HandlerPriorityAttribute).Assembly.Location));

        test.TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(
            typeof(MessageContractAttribute).Assembly.Location));

        test.TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(
            typeof(ITransportPublisher).Assembly.Location));

        foreach (var reference in references)
        {
            test.TestState.AdditionalReferences.Add(reference);
        }

        foreach (var (descriptor, markupLocation, arguments) in expectedDiagnostics)
        {
            var expected = new DiagnosticResult(descriptor).WithArguments(arguments);

            if (markupLocation.HasValue)
            {
                expected = expected.WithLocation(markupLocation.Value);
            }

            test.ExpectedDiagnostics.Add(expected);
        }

        return test.RunAsync(CancellationToken.None);
    }
}
