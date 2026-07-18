using LiteBus.Commands.Abstractions;
using LiteBus.Events.Abstractions;
using LiteBus.Messaging.Abstractions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;

namespace LiteBus.Analyzers.UnitTests;

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
            typeof(ICommand).Assembly.Location);

        var messagingReference = MetadataReference.CreateFromFile(
            typeof(HandlerPriorityAttribute).Assembly.Location);

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
                        AdditionalReferences = { commandsReference, messagingReference }
                    }
                },
                ExpectedDiagnostics =
                {
                    new DiagnosticResult(DiagnosticDescriptors.DuplicateHandlerAcrossAssemblies)
                        .WithSpan("HandlerA.cs", 7, 21, 7, 38)
                        .WithArguments("SharedHandlerName", "AssemblyB", "TestProject")
                }
            }
        };

        await test.RunAsync(CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    ///     Verifies duplicate handler names in the same assembly do not produce LB1012.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public async Task DuplicateHandlerNameInSameAssembly_ShouldNotReport()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;

                              public sealed class SharedHandlerName : ICommandHandler<CommandA>
                              {
                                  public Task HandleAsync(CommandA message, CancellationToken cancellationToken = default) => Task.CompletedTask;
                              }

                              public sealed class SharedHandlerNameForB : ICommandHandler<CommandB>
                              {
                                  public Task HandleAsync(CommandB message, CancellationToken cancellationToken = default) => Task.CompletedTask;
                              }

                              public sealed record CommandA : ICommand;
                              public sealed record CommandB : ICommand;
                              """;

        await AnalyzerTest.VerifyNoDiagnosticsAsync<DuplicateHandlerAcrossAssembliesAnalyzer>(source).ConfigureAwait(false);
    }

    /// <summary>
    ///     Verifies duplicate event handler names across assemblies produce LB1012.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public async Task DuplicateEventHandlerAcrossAssemblies_ShouldReportWhenNameExistsInTwoAssemblies()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Events.Abstractions;

                              namespace AssemblyA;

                              public sealed class SharedEventHandler : IEventHandler<EventA>
                              {
                                  public Task HandleAsync(EventA message, CancellationToken cancellationToken = default) => Task.CompletedTask;
                              }

                              public sealed record EventA : IEvent;
                              """;

        const string otherAssembly = """
                                     using System.Threading;
                                     using System.Threading.Tasks;
                                     using LiteBus.Events.Abstractions;

                                     namespace AssemblyB;

                                     public sealed class SharedEventHandler : IEventHandler<EventB>
                                     {
                                         public Task HandleAsync(EventB message, CancellationToken cancellationToken = default) => Task.CompletedTask;
                                     }

                                     public sealed record EventB : IEvent;
                                     """;

        var eventsReference = MetadataReference.CreateFromFile(
            typeof(IEvent).Assembly.Location);

        var messagingReference = MetadataReference.CreateFromFile(
            typeof(HandlerPriorityAttribute).Assembly.Location);

        var test = new CSharpAnalyzerTest<DuplicateHandlerAcrossAssembliesAnalyzer, DefaultVerifier>
        {
            ReferenceAssemblies = AnalyzerTest.Net10ReferenceAssemblies,
            TestState =
            {
                Sources = { ("HandlerA.cs", source) },
                AdditionalReferences = { eventsReference, messagingReference },
                AdditionalProjectReferences = { "AssemblyB" },
                AdditionalProjects =
                {
                    ["AssemblyB"] =
                    {
                        Sources = { ("HandlerB.cs", otherAssembly) },
                        ReferenceAssemblies = AnalyzerTest.Net10ReferenceAssemblies,
                        AdditionalReferences = { eventsReference, messagingReference }
                    }
                },
                ExpectedDiagnostics =
                {
                    new DiagnosticResult(DiagnosticDescriptors.DuplicateHandlerAcrossAssemblies)
                        .WithSpan("HandlerA.cs", 7, 21, 7, 39)
                        .WithArguments("SharedEventHandler", "AssemblyB", "TestProject")
                }
            }
        };

        await test.RunAsync(CancellationToken.None).ConfigureAwait(false);
    }
}