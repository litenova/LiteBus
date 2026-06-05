using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.Testing.Verifiers;
using Xunit;

namespace LiteBus.Analyzers.Tests;

/// <summary>
///     Tests for the <see cref="DuplicateHandlerAcrossAssembliesAnalyzer" /> rule.
/// </summary>
public sealed class DuplicateHandlerAcrossAssembliesAnalyzerTests
{
    /// <summary>
    ///     Verifies duplicate handler names across assemblies produce LB1012.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public async Task DuplicateHandlerAcrossAssemblies_ShouldReportWhenNameExistsInTwoAssemblies()
    {
        const string source = """
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

        const string otherAssembly = """
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

        var commandsReference = MetadataReference.CreateFromFile(
            typeof(LiteBus.Commands.Abstractions.ICommand).Assembly.Location);
        var messagingReference = MetadataReference.CreateFromFile(
            typeof(LiteBus.Messaging.Abstractions.HandlerPriorityAttribute).Assembly.Location);

        var test = new CSharpAnalyzerTest<DuplicateHandlerAcrossAssembliesAnalyzer, DefaultVerifier>
        {
            ReferenceAssemblies = AnalyzerTest.Net10ReferenceAssemblies,
            TestState =
            {
                Sources = { ("HandlerA.cs", source) },
                AdditionalReferences = { commandsReference, messagingReference },
                AdditionalProjectReferences = { "AssemblyB" },
                AdditionalProjects =
                {
                    ["AssemblyB"] =
                    {
                        Sources = { ("HandlerB.cs", otherAssembly) },
                        ReferenceAssemblies = AnalyzerTest.Net10ReferenceAssemblies,
                        AdditionalReferences = { commandsReference, messagingReference },
                    },
                },
                ExpectedDiagnostics =
                {
                    new DiagnosticResult(DiagnosticDescriptors.DuplicateHandlerAcrossAssemblies)
                        .WithSpan("HandlerA.cs", 7, 21, 7, 38)
                        .WithArguments("SharedHandlerName", "AssemblyB", "TestProject"),
                },
            },
        };

        await test.RunAsync(CancellationToken.None);
    }
}
