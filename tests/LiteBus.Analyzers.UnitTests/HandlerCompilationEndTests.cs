using System.Collections.Immutable;
using System.Globalization;
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace LiteBus.Analyzers.UnitTests;

/// <summary>
///     Compilation-end tests for the duplicate and missing handler analyzers.
/// </summary>
public sealed class HandlerCompilationEndTests
{
    /// <summary>
    ///     Verifies that duplicate command handlers in two files report the second file's handler.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public async Task DuplicateCommandHandlersAcrossTwoFiles_ReportsSecondFileHandler()
    {
        const string file0 = """
                             using System.Threading;
                             using System.Threading.Tasks;
                             using LiteBus.Commands.Abstractions;

                             public sealed record CreateUserCommand(string Name) : ICommand;

                             public sealed class FirstCreateUserCommandHandler : ICommandHandler<CreateUserCommand>
                             {
                                 public Task HandleAsync(CreateUserCommand command, CancellationToken cancellationToken = default)
                                     => Task.CompletedTask;
                             }
                             """;

        const string file1 = """
                             using System.Threading;
                             using System.Threading.Tasks;
                             using LiteBus.Commands.Abstractions;

                             public sealed class {|#0:SecondCreateUserCommandHandler|} : ICommandHandler<CreateUserCommand>
                             {
                                 public Task HandleAsync(CreateUserCommand command, CancellationToken cancellationToken = default)
                                     => Task.CompletedTask;
                             }
                             """;

        await AnalyzerTest.VerifyMultiFileAsync<DuplicateCommandHandlerAnalyzer>(
            [file0, file1],
            (DiagnosticDescriptors.DuplicateCommandHandler, 0, ["CreateUserCommand", "FirstCreateUserCommandHandler", "SecondCreateUserCommandHandler"])).ConfigureAwait(false);
    }

    /// <summary>
    ///     Verifies that distinct command handlers in two files produce no diagnostic.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public async Task DistinctCommandHandlersAcrossTwoFiles_ProduceNoDiagnostic()
    {
        const string file0 = """
                             using System.Threading;
                             using System.Threading.Tasks;
                             using LiteBus.Commands.Abstractions;

                             public sealed record CreateUserCommand(string Name) : ICommand;

                             public sealed class CreateUserCommandHandler : ICommandHandler<CreateUserCommand>
                             {
                                 public Task HandleAsync(CreateUserCommand command, CancellationToken cancellationToken = default)
                                     => Task.CompletedTask;
                             }
                             """;

        const string file1 = """
                             using System.Threading;
                             using System.Threading.Tasks;
                             using LiteBus.Commands.Abstractions;

                             public sealed record DeleteUserCommand(int UserId) : ICommand;

                             public sealed class DeleteUserCommandHandler : ICommandHandler<DeleteUserCommand>
                             {
                                 public Task HandleAsync(DeleteUserCommand command, CancellationToken cancellationToken = default)
                                     => Task.CompletedTask;
                             }
                             """;

        await AnalyzerTest.VerifyMultiFileAsync<DuplicateCommandHandlerAnalyzer>(
            [file0, file1]).ConfigureAwait(false);
    }

    /// <summary>
    ///     Verifies that per-file analysis reports no duplicate command handler while full analysis does.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public async Task DuplicateCommandHandler_PerFileAnalysisReportsNothingWhileFullAnalysisReports()
    {
        const string file0 = """
                             using System.Threading;
                             using System.Threading.Tasks;
                             using LiteBus.Commands.Abstractions;

                             public sealed record CreateUserCommand(string Name) : ICommand;

                             public sealed class FirstCreateUserCommandHandler : ICommandHandler<CreateUserCommand>
                             {
                                 public Task HandleAsync(CreateUserCommand command, CancellationToken cancellationToken = default)
                                     => Task.CompletedTask;
                             }
                             """;

        const string file1 = """
                             using System.Threading;
                             using System.Threading.Tasks;
                             using LiteBus.Commands.Abstractions;

                             public sealed class SecondCreateUserCommandHandler : ICommandHandler<CreateUserCommand>
                             {
                                 public Task HandleAsync(CreateUserCommand command, CancellationToken cancellationToken = default)
                                     => Task.CompletedTask;
                             }
                             """;

        const string marked = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;

                              public sealed class {|#0:SecondCreateUserCommandHandler|} : ICommandHandler<CreateUserCommand>
                              {
                                  public Task HandleAsync(CreateUserCommand command, CancellationToken cancellationToken = default)
                                      => Task.CompletedTask;
                              }
                              """;

        var compilation = await AnalyzerTest.CreateCompilationAsync([file0, file1]).ConfigureAwait(false);

        var perFile = await AnalyzerTest.GetPerFileDiagnosticsAsync(
            compilation,
            1,
            new DuplicateCommandHandlerAnalyzer()).ConfigureAwait(false);

        Assert.DoesNotContain(perFile, diagnostic => diagnostic.Id == DiagnosticIds.DuplicateCommandHandler);

        await AnalyzerTest.VerifyMultiFileAsync<DuplicateCommandHandlerAnalyzer>(
            [file0, marked],
            (DiagnosticDescriptors.DuplicateCommandHandler, 0, ["CreateUserCommand", "FirstCreateUserCommandHandler", "SecondCreateUserCommandHandler"])).ConfigureAwait(false);
    }

    /// <summary>
    ///     Verifies that duplicate query handlers in two files report the second file's handler.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public async Task DuplicateQueryHandlersAcrossTwoFiles_ReportsSecondFileHandler()
    {
        const string file0 = """
                             using System.Threading;
                             using System.Threading.Tasks;
                             using LiteBus.Queries.Abstractions;

                             public sealed record GetUserQuery(int UserId) : IQuery<string>;

                             public sealed class FirstGetUserQueryHandler : IQueryHandler<GetUserQuery, string>
                             {
                                 public Task<string> HandleAsync(GetUserQuery query, CancellationToken cancellationToken = default)
                                     => Task.FromResult("Ada");
                             }
                             """;

        const string file1 = """
                             using System.Threading;
                             using System.Threading.Tasks;
                             using LiteBus.Queries.Abstractions;

                             public sealed class {|#0:SecondGetUserQueryHandler|} : IQueryHandler<GetUserQuery, string>
                             {
                                 public Task<string> HandleAsync(GetUserQuery query, CancellationToken cancellationToken = default)
                                     => Task.FromResult("Bob");
                             }
                             """;

        await AnalyzerTest.VerifyMultiFileAsync<DuplicateQueryHandlerAnalyzer>(
            [file0, file1],
            (DiagnosticDescriptors.DuplicateQueryHandler, 0, ["GetUserQuery", "FirstGetUserQueryHandler", "SecondGetUserQueryHandler"])).ConfigureAwait(false);
    }

    /// <summary>
    ///     Verifies that distinct query handlers in two files produce no diagnostic.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public async Task DistinctQueryHandlersAcrossTwoFiles_ProduceNoDiagnostic()
    {
        const string file0 = """
                             using System.Threading;
                             using System.Threading.Tasks;
                             using LiteBus.Queries.Abstractions;

                             public sealed record GetUserQuery(int UserId) : IQuery<string>;

                             public sealed class GetUserQueryHandler : IQueryHandler<GetUserQuery, string>
                             {
                                 public Task<string> HandleAsync(GetUserQuery query, CancellationToken cancellationToken = default)
                                     => Task.FromResult("Ada");
                             }
                             """;

        const string file1 = """
                             using System.Threading;
                             using System.Threading.Tasks;
                             using LiteBus.Queries.Abstractions;

                             public sealed record GetOrderQuery(int OrderId) : IQuery<string>;

                             public sealed class GetOrderQueryHandler : IQueryHandler<GetOrderQuery, string>
                             {
                                 public Task<string> HandleAsync(GetOrderQuery query, CancellationToken cancellationToken = default)
                                     => Task.FromResult("order");
                             }
                             """;

        await AnalyzerTest.VerifyMultiFileAsync<DuplicateQueryHandlerAnalyzer>(
            [file0, file1]).ConfigureAwait(false);
    }

    /// <summary>
    ///     Verifies that per-file analysis reports no duplicate query handler while full analysis does.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public async Task DuplicateQueryHandler_PerFileAnalysisReportsNothingWhileFullAnalysisReports()
    {
        const string file0 = """
                             using System.Threading;
                             using System.Threading.Tasks;
                             using LiteBus.Queries.Abstractions;

                             public sealed record GetUserQuery(int UserId) : IQuery<string>;

                             public sealed class FirstGetUserQueryHandler : IQueryHandler<GetUserQuery, string>
                             {
                                 public Task<string> HandleAsync(GetUserQuery query, CancellationToken cancellationToken = default)
                                     => Task.FromResult("Ada");
                             }
                             """;

        const string file1 = """
                             using System.Threading;
                             using System.Threading.Tasks;
                             using LiteBus.Queries.Abstractions;

                             public sealed class SecondGetUserQueryHandler : IQueryHandler<GetUserQuery, string>
                             {
                                 public Task<string> HandleAsync(GetUserQuery query, CancellationToken cancellationToken = default)
                                     => Task.FromResult("Bob");
                             }
                             """;

        const string marked = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Queries.Abstractions;

                              public sealed class {|#0:SecondGetUserQueryHandler|} : IQueryHandler<GetUserQuery, string>
                              {
                                  public Task<string> HandleAsync(GetUserQuery query, CancellationToken cancellationToken = default)
                                      => Task.FromResult("Bob");
                              }
                              """;

        var compilation = await AnalyzerTest.CreateCompilationAsync([file0, file1]).ConfigureAwait(false);

        var perFile = await AnalyzerTest.GetPerFileDiagnosticsAsync(
            compilation,
            1,
            new DuplicateQueryHandlerAnalyzer()).ConfigureAwait(false);

        Assert.DoesNotContain(perFile, diagnostic => diagnostic.Id == DiagnosticIds.DuplicateQueryHandler);

        await AnalyzerTest.VerifyMultiFileAsync<DuplicateQueryHandlerAnalyzer>(
            [file0, marked],
            (DiagnosticDescriptors.DuplicateQueryHandler, 0, ["GetUserQuery", "FirstGetUserQueryHandler", "SecondGetUserQueryHandler"])).ConfigureAwait(false);
    }

    /// <summary>
    ///     Verifies that a handler duplicated across assemblies is reported when the source handler spans two files.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public async Task DuplicateHandlerAcrossAssemblies_WithSourceHandlerAcrossTwoFiles_ReportsDiagnostic()
    {
        var compilation = await CreateCrossAssemblyCompilationAsync().ConfigureAwait(false);

        var diagnostics = await compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new DuplicateHandlerAcrossAssembliesAnalyzer()))
            .GetAnalyzerDiagnosticsAsync()
            .ConfigureAwait(false);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.DuplicateHandlerAcrossAssemblies, diagnostic.Id);
        Assert.Equal("/0/Test0.cs", diagnostic.Location.SourceTree?.FilePath);
        var message = diagnostic.GetMessage(CultureInfo.InvariantCulture);
        Assert.Contains("SharedHandlerName", message, StringComparison.Ordinal);
        Assert.Contains("AssemblyB", message, StringComparison.Ordinal);
        Assert.Contains("LiteBusAnalyzerCompilation", message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Verifies that same-name handlers in two files of one assembly produce no diagnostic.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public async Task SameNameHandlersInTwoFilesOfOneAssembly_ProduceNoDiagnostic()
    {
        const string file0 = """
                             using System.Threading;
                             using System.Threading.Tasks;
                             using LiteBus.Commands.Abstractions;

                             namespace AssemblyA;

                             public sealed class SharedHandlerName : ICommandHandler<CommandA>
                             {
                                 public Task HandleAsync(CommandA message, CancellationToken cancellationToken = default) => Task.CompletedTask;
                             }

                             public sealed record CommandA : ICommand;
                             """;

        const string file1 = """
                             using System.Threading;
                             using System.Threading.Tasks;
                             using LiteBus.Commands.Abstractions;

                             namespace AssemblyB;

                             public sealed class SharedHandlerName : ICommandHandler<CommandB>
                             {
                                 public Task HandleAsync(CommandB message, CancellationToken cancellationToken = default) => Task.CompletedTask;
                             }

                             public sealed record CommandB : ICommand;
                             """;

        await AnalyzerTest.VerifyMultiFileAsync<DuplicateHandlerAcrossAssembliesAnalyzer>(
            [file0, file1]).ConfigureAwait(false);
    }

    /// <summary>
    ///     Verifies that per-file analysis reports no cross-assembly duplicate while full analysis does.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public async Task DuplicateHandlerAcrossAssemblies_PerFileAnalysisReportsNothingWhileFullAnalysisReports()
    {
        var compilation = await CreateCrossAssemblyCompilationAsync().ConfigureAwait(false);

        var perFile = await AnalyzerTest.GetPerFileDiagnosticsAsync(
            compilation,
            0,
            new DuplicateHandlerAcrossAssembliesAnalyzer()).ConfigureAwait(false);

        Assert.DoesNotContain(perFile, diagnostic => diagnostic.Id == DiagnosticIds.DuplicateHandlerAcrossAssemblies);

        var diagnostics = await compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new DuplicateHandlerAcrossAssembliesAnalyzer()))
            .GetAnalyzerDiagnosticsAsync()
            .ConfigureAwait(false);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.DuplicateHandlerAcrossAssemblies, diagnostic.Id);
    }

    /// <summary>
    ///     Verifies that a command without a handler is reported when an unrelated handler lives in another file.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public async Task CommandWithoutHandlerAcrossTwoFiles_ProducesDiagnostic()
    {
        const string file0 = """
                             using LiteBus.Commands.Abstractions;

                             public sealed record {|#0:CreateUserCommand|}(string Name) : ICommand;
                             """;

        const string file1 = """
                             using System.Threading;
                             using System.Threading.Tasks;
                             using LiteBus.Commands.Abstractions;

                             public sealed record OtherCommand(string Name) : ICommand;

                             public sealed class OtherCommandHandler : ICommandHandler<OtherCommand>
                             {
                                 public Task HandleAsync(OtherCommand command, CancellationToken cancellationToken = default)
                                     => Task.CompletedTask;
                             }
                             """;

        await AnalyzerTest.VerifyMultiFileAsync<MissingCommandHandlerAnalyzer>(
            [file0, file1],
            (DiagnosticDescriptors.MissingCommandHandler, 0, ["CreateUserCommand"])).ConfigureAwait(false);
    }

    /// <summary>
    ///     Verifies that a command with its handler in another file produces no diagnostic.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public async Task CommandWithHandlerInAnotherFile_ProducesNoDiagnostic()
    {
        const string file0 = """
                             using LiteBus.Commands.Abstractions;

                             public sealed record CreateUserCommand(string Name) : ICommand;
                             """;

        const string file1 = """
                             using System.Threading;
                             using System.Threading.Tasks;
                             using LiteBus.Commands.Abstractions;

                             public sealed class CreateUserCommandHandler : ICommandHandler<CreateUserCommand>
                             {
                                 public Task HandleAsync(CreateUserCommand command, CancellationToken cancellationToken = default)
                                     => Task.CompletedTask;
                             }
                             """;

        await AnalyzerTest.VerifyMultiFileAsync<MissingCommandHandlerAnalyzer>(
            [file0, file1]).ConfigureAwait(false);
    }

    /// <summary>
    ///     Verifies that per-file analysis reports no missing command handler while full analysis does.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public async Task MissingCommandHandler_PerFileAnalysisReportsNothingWhileFullAnalysisReports()
    {
        const string file0 = """
                             using LiteBus.Commands.Abstractions;

                             public sealed record CreateUserCommand(string Name) : ICommand;
                             """;

        const string file1 = """
                             public sealed class Placeholder
                             {
                             }
                             """;

        const string marked = """
                              using LiteBus.Commands.Abstractions;

                              public sealed record {|#0:CreateUserCommand|}(string Name) : ICommand;
                              """;

        var compilation = await AnalyzerTest.CreateCompilationAsync([file0, file1]).ConfigureAwait(false);

        var perFile = await AnalyzerTest.GetPerFileDiagnosticsAsync(
            compilation,
            0,
            new MissingCommandHandlerAnalyzer()).ConfigureAwait(false);

        Assert.DoesNotContain(perFile, diagnostic => diagnostic.Id == DiagnosticIds.MissingCommandHandler);

        await AnalyzerTest.VerifyMultiFileAsync<MissingCommandHandlerAnalyzer>(
            [marked, file1],
            (DiagnosticDescriptors.MissingCommandHandler, 0, ["CreateUserCommand"])).ConfigureAwait(false);
    }

    /// <summary>
    ///     Verifies that a query without a handler is reported when an unrelated handler lives in another file.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public async Task QueryWithoutHandlerAcrossTwoFiles_ProducesDiagnostic()
    {
        const string file0 = """
                             using LiteBus.Queries.Abstractions;

                             public sealed record {|#0:GetUserQuery|}(int UserId) : IQuery<string>;
                             """;

        const string file1 = """
                             using System.Threading;
                             using System.Threading.Tasks;
                             using LiteBus.Queries.Abstractions;

                             public sealed record GetOrderQuery(int OrderId) : IQuery<string>;

                             public sealed class GetOrderQueryHandler : IQueryHandler<GetOrderQuery, string>
                             {
                                 public Task<string> HandleAsync(GetOrderQuery query, CancellationToken cancellationToken = default)
                                     => Task.FromResult("order");
                             }
                             """;

        await AnalyzerTest.VerifyMultiFileAsync<MissingQueryHandlerAnalyzer>(
            [file0, file1],
            (DiagnosticDescriptors.MissingQueryHandler, 0, ["GetUserQuery"])).ConfigureAwait(false);
    }

    /// <summary>
    ///     Verifies that a query with its handler in another file produces no diagnostic.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public async Task QueryWithHandlerInAnotherFile_ProducesNoDiagnostic()
    {
        const string file0 = """
                             using LiteBus.Queries.Abstractions;

                             public sealed record GetUserQuery(int UserId) : IQuery<string>;
                             """;

        const string file1 = """
                             using System.Threading;
                             using System.Threading.Tasks;
                             using LiteBus.Queries.Abstractions;

                             public sealed class GetUserQueryHandler : IQueryHandler<GetUserQuery, string>
                             {
                                 public Task<string> HandleAsync(GetUserQuery query, CancellationToken cancellationToken = default)
                                     => Task.FromResult("Ada");
                             }
                             """;

        await AnalyzerTest.VerifyMultiFileAsync<MissingQueryHandlerAnalyzer>(
            [file0, file1]).ConfigureAwait(false);
    }

    /// <summary>
    ///     Verifies that per-file analysis reports no missing query handler while full analysis does.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public async Task MissingQueryHandler_PerFileAnalysisReportsNothingWhileFullAnalysisReports()
    {
        const string file0 = """
                             using LiteBus.Queries.Abstractions;

                             public sealed record GetUserQuery(int UserId) : IQuery<string>;
                             """;

        const string file1 = """
                             public sealed class Placeholder
                             {
                             }
                             """;

        const string marked = """
                              using LiteBus.Queries.Abstractions;

                              public sealed record {|#0:GetUserQuery|}(int UserId) : IQuery<string>;
                              """;

        var compilation = await AnalyzerTest.CreateCompilationAsync([file0, file1]).ConfigureAwait(false);

        var perFile = await AnalyzerTest.GetPerFileDiagnosticsAsync(
            compilation,
            0,
            new MissingQueryHandlerAnalyzer()).ConfigureAwait(false);

        Assert.DoesNotContain(perFile, diagnostic => diagnostic.Id == DiagnosticIds.MissingQueryHandler);

        await AnalyzerTest.VerifyMultiFileAsync<MissingQueryHandlerAnalyzer>(
            [marked, file1],
            (DiagnosticDescriptors.MissingQueryHandler, 0, ["GetUserQuery"])).ConfigureAwait(false);
    }

    private static async Task<Compilation> CreateCrossAssemblyCompilationAsync()
    {
        const string file0 = """
                             using System.Threading;
                             using System.Threading.Tasks;
                             using LiteBus.Commands.Abstractions;

                             namespace AssemblyA;

                             public sealed class SharedHandlerName : ICommandHandler<CommandA>
                             {
                                 public Task HandleAsync(CommandA message, CancellationToken cancellationToken = default) => Task.CompletedTask;
                             }
                             """;

        const string file1 = """
                             using LiteBus.Commands.Abstractions;

                             namespace AssemblyA;

                             public sealed record CommandA : ICommand;
                             """;

        const string referencedAssembly = """
                                          using System.Threading;
                                          using System.Threading.Tasks;
                                          using LiteBus.Commands.Abstractions;

                                          namespace AssemblyB;

                                          public sealed class SharedHandlerName : ICommandHandler<CommandB>
                                          {
                                              public Task HandleAsync(CommandB message, CancellationToken cancellationToken = default) => Task.CompletedTask;
                                          }

                                          public sealed record CommandB : ICommand;
                                          """;

        var commandsReference = MetadataReference.CreateFromFile(typeof(ICommand).Assembly.Location);
        var messagingReference = MetadataReference.CreateFromFile(typeof(HandlerPriorityAttribute).Assembly.Location);
        var referencedProject = AnalyzerTest.CompileToMetadataReference(
            "AssemblyB",
            referencedAssembly,
            commandsReference,
            messagingReference);

        Compilation compilation = await AnalyzerTest.CreateCompilationAsync([file0, file1]).ConfigureAwait(false);
        return compilation.AddReferences(referencedProject);
    }
}
