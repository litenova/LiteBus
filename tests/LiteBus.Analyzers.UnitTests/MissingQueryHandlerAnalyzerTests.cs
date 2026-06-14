using LiteBus.Messaging.Abstractions;
using LiteBus.Queries.Abstractions;
using Microsoft.CodeAnalysis;

namespace LiteBus.Analyzers.UnitTests;

/// <summary>
///     Tests for the <see cref="MissingQueryHandlerAnalyzer" /> rule.
/// </summary>
public sealed class MissingQueryHandlerAnalyzerTests
{
    /// <summary>
    ///     Verifies that a query with a handler produces no diagnostic.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task QueryWithHandler_ProducesNoDiagnostic()
    {
        const string source = """
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

        return AnalyzerTest.VerifyNoDiagnosticsAsync<MissingQueryHandlerAnalyzer>(source);
    }

    /// <summary>
    ///     Verifies that a query without a handler produces LB1009.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task QueryWithoutHandler_ProducesDiagnostic()
    {
        const string source = """
                              using LiteBus.Queries.Abstractions;

                              public sealed record {|#0:GetUserQuery|}(int UserId) : IQuery<string>;
                              """;

        return AnalyzerTest.VerifyDiagnosticAsync<MissingQueryHandlerAnalyzer>(
            source,
            DiagnosticDescriptors.MissingQueryHandler,
            0,
            "GetUserQuery");
    }

    /// <summary>
    ///     Verifies that a stream query with a stream query handler produces no diagnostic.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task StreamQueryWithHandler_ProducesNoDiagnostic()
    {
        const string source = """
                              using System.Collections.Generic;
                              using System.Threading;
                              using LiteBus.Queries.Abstractions;

                              public sealed record StreamUsersQuery(int PageSize) : IStreamQuery<string>;

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

        return AnalyzerTest.VerifyNoDiagnosticsAsync<MissingQueryHandlerAnalyzer>(source);
    }

    /// <summary>
    ///     Verifies that a stream query without a stream query handler produces LB1009.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task StreamQueryWithoutHandler_ProducesDiagnostic()
    {
        const string source = """
                              using LiteBus.Queries.Abstractions;

                              public sealed record {|#0:StreamUsersQuery|}(int PageSize) : IStreamQuery<string>;
                              """;

        return AnalyzerTest.VerifyDiagnosticAsync<MissingQueryHandlerAnalyzer>(
            source,
            DiagnosticDescriptors.MissingQueryHandler,
            0,
            "StreamUsersQuery");
    }

    /// <summary>
    ///     Verifies that a query declared in a referenced assembly with a handler in the main project produces no diagnostic.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task QueryInReferencedAssemblyWithHandler_ProducesNoDiagnostic()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Queries.Abstractions;

                              public sealed class GetUserQueryHandler : IQueryHandler<Queries.GetUserQuery, string>
                              {
                                  public Task<string> HandleAsync(Queries.GetUserQuery query, CancellationToken cancellationToken = default)
                                      => Task.FromResult("Ada");
                              }
                              """;

        const string referencedAssembly = """
                                          using LiteBus.Queries.Abstractions;

                                          namespace Queries;

                                          public sealed record GetUserQuery(int UserId) : IQuery<string>;
                                          """;

        var queriesReference = MetadataReference.CreateFromFile(typeof(IQuery<>).Assembly.Location);
        var messagingReference = MetadataReference.CreateFromFile(typeof(HandlerPriorityAttribute).Assembly.Location);
        var referencedProject = AnalyzerTest.CompileToMetadataReference(
            "QueriesAssembly",
            referencedAssembly,
            queriesReference,
            messagingReference);

        return AnalyzerTest.VerifyNoDiagnosticsWithReferencesAsync<MissingQueryHandlerAnalyzer>(
            source,
            queriesReference,
            messagingReference,
            referencedProject);
    }

    /// <summary>
    ///     Verifies that a query declared only in a referenced assembly without a handler produces LB1009.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task QueryInReferencedAssemblyWithoutHandler_ProducesDiagnostic()
    {
        const string source = """
                              public sealed class Placeholder
                              {
                              }
                              """;

        const string referencedAssembly = """
                                          using LiteBus.Queries.Abstractions;

                                          namespace Queries;

                                          public sealed record GetUserQuery(int UserId) : IQuery<string>;
                                          """;

        var queriesReference = MetadataReference.CreateFromFile(typeof(IQuery<>).Assembly.Location);
        var messagingReference = MetadataReference.CreateFromFile(typeof(HandlerPriorityAttribute).Assembly.Location);
        var referencedProject = AnalyzerTest.CompileToMetadataReference(
            "QueriesAssembly",
            referencedAssembly,
            queriesReference,
            messagingReference);

        return AnalyzerTest.VerifyDiagnosticWithReferencesAsync<MissingQueryHandlerAnalyzer>(
            source,
            DiagnosticDescriptors.MissingQueryHandler,
            ["GetUserQuery"],
            queriesReference,
            messagingReference,
            referencedProject);
    }

    /// <summary>
    ///     Verifies that a stream query declared in a referenced assembly with a stream handler produces no diagnostic.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task StreamQueryInReferencedAssemblyWithHandler_ProducesNoDiagnostic()
    {
        const string source = """
                              using System.Collections.Generic;
                              using System.Threading;
                              using LiteBus.Queries.Abstractions;

                              public sealed class StreamUsersQueryHandler : IStreamQueryHandler<Queries.StreamUsersQuery, string>
                              {
                                  public IAsyncEnumerable<string> StreamAsync(Queries.StreamUsersQuery query, CancellationToken cancellationToken = default)
                                      => Empty();

                                  private static async IAsyncEnumerable<string> Empty()
                                  {
                                      yield break;
                                  }
                              }
                              """;

        const string referencedAssembly = """
                                          using LiteBus.Queries.Abstractions;

                                          namespace Queries;

                                          public sealed record StreamUsersQuery(int PageSize) : IStreamQuery<string>;
                                          """;

        var queriesReference = MetadataReference.CreateFromFile(typeof(IQuery<>).Assembly.Location);
        var messagingReference = MetadataReference.CreateFromFile(typeof(HandlerPriorityAttribute).Assembly.Location);
        var referencedProject = AnalyzerTest.CompileToMetadataReference(
            "QueriesAssembly",
            referencedAssembly,
            queriesReference,
            messagingReference);

        return AnalyzerTest.VerifyNoDiagnosticsWithReferencesAsync<MissingQueryHandlerAnalyzer>(
            source,
            queriesReference,
            messagingReference,
            referencedProject);
    }

    /// <summary>
    ///     Verifies that a stream query declared only in a referenced assembly without a handler produces LB1009.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task StreamQueryInReferencedAssemblyWithoutHandler_ProducesDiagnostic()
    {
        const string source = """
                              public sealed class Placeholder
                              {
                              }
                              """;

        const string referencedAssembly = """
                                          using LiteBus.Queries.Abstractions;

                                          namespace Queries;

                                          public sealed record StreamUsersQuery(int PageSize) : IStreamQuery<string>;
                                          """;

        var queriesReference = MetadataReference.CreateFromFile(typeof(IQuery<>).Assembly.Location);
        var messagingReference = MetadataReference.CreateFromFile(typeof(HandlerPriorityAttribute).Assembly.Location);
        var referencedProject = AnalyzerTest.CompileToMetadataReference(
            "QueriesAssembly",
            referencedAssembly,
            queriesReference,
            messagingReference);

        return AnalyzerTest.VerifyDiagnosticWithReferencesAsync<MissingQueryHandlerAnalyzer>(
            source,
            DiagnosticDescriptors.MissingQueryHandler,
            ["StreamUsersQuery"],
            queriesReference,
            messagingReference,
            referencedProject);
    }
}