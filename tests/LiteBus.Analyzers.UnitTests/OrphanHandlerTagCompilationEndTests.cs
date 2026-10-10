namespace LiteBus.Analyzers.UnitTests;

/// <summary>
///     Compilation-end tests for the <see cref="OrphanHandlerTagAnalyzer" /> rule.
/// </summary>
public sealed class OrphanHandlerTagCompilationEndTests
{
    /// <summary>
    ///     Verifies that an orphan tag is still reported when the handler and an unrelated reference live in two files.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public async Task OrphanTagAcrossFiles_ProducesDiagnostic()
    {
        const string handlerSource = """
                                     using System.Threading;
                                     using System.Threading.Tasks;
                                     using LiteBus.Commands.Abstractions;
                                     using LiteBus.Messaging.Abstractions;

                                     [HandlerTag("orphan")]
                                     public sealed class {|#0:OrphanTaggedHandler|} : ICommandHandler<SampleCommand>
                                     {
                                         public Task HandleAsync(SampleCommand message, CancellationToken cancellationToken = default) => Task.CompletedTask;
                                     }

                                     public sealed record SampleCommand : ICommand;
                                     """;

        const string senderSource = """
                                    using System.Collections.Generic;
                                    using LiteBus.Commands.Abstractions;

                                    public sealed class Sender
                                    {
                                        public void Send()
                                        {
                                            var settings = new CommandMediationSettings
                                            {
                                                Routing = new CommandRoutingSettings
                                                {
                                                    Tags = new List<string> { "other" }
                                                }
                                            };
                                        }
                                    }
                                    """;

        await AnalyzerTest.VerifyMultiFileAsync<OrphanHandlerTagAnalyzer>(
            [handlerSource, senderSource],
            (DiagnosticDescriptors.OrphanHandlerTag, 0, new object[] { "OrphanTaggedHandler", "orphan" })).ConfigureAwait(false);
    }

    /// <summary>
    ///     Verifies that no diagnostic is reported when the tag reference lives in another file than the handler.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public async Task TagReferencedInAnotherFile_ProducesNoDiagnostic()
    {
        const string handlerSource = """
                                     using System.Threading;
                                     using System.Threading.Tasks;
                                     using LiteBus.Commands.Abstractions;
                                     using LiteBus.Messaging.Abstractions;

                                     [HandlerTag("frontend")]
                                     public sealed class TaggedHandler : ICommandHandler<SampleCommand>
                                     {
                                         public Task HandleAsync(SampleCommand message, CancellationToken cancellationToken = default) => Task.CompletedTask;
                                     }

                                     public sealed record SampleCommand : ICommand;
                                     """;

        const string senderSource = """
                                    using System.Collections.Generic;
                                    using LiteBus.Commands.Abstractions;

                                    public sealed class Sender
                                    {
                                        public void Send()
                                        {
                                            var settings = new CommandMediationSettings
                                            {
                                                Routing = new CommandRoutingSettings
                                                {
                                                    Tags = new List<string> { "frontend" }
                                                }
                                            };
                                        }
                                    }
                                    """;

        await AnalyzerTest.VerifyMultiFileAsync<OrphanHandlerTagAnalyzer>(
            [handlerSource, senderSource]).ConfigureAwait(false);
    }

    /// <summary>
    ///     Verifies that per-file analysis reports nothing while full analysis reports the orphan tag.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public async Task PerFileAnalysis_ReportsNothingWhileFullAnalysisReports()
    {
        const string handlerSource = """
                                     using System.Threading;
                                     using System.Threading.Tasks;
                                     using LiteBus.Commands.Abstractions;
                                     using LiteBus.Messaging.Abstractions;

                                     [HandlerTag("orphan")]
                                     public sealed class OrphanTaggedHandler : ICommandHandler<SampleCommand>
                                     {
                                         public Task HandleAsync(SampleCommand message, CancellationToken cancellationToken = default) => Task.CompletedTask;
                                     }

                                     public sealed record SampleCommand : ICommand;
                                     """;

        const string senderSource = """
                                    using System.Collections.Generic;
                                    using LiteBus.Commands.Abstractions;

                                    public sealed class Sender
                                    {
                                        public void Send()
                                        {
                                            var settings = new CommandMediationSettings
                                            {
                                                Routing = new CommandRoutingSettings
                                                {
                                                    Tags = new List<string> { "other" }
                                                }
                                            };
                                        }
                                    }
                                    """;

        const string handlerMarkup = """
                                     using System.Threading;
                                     using System.Threading.Tasks;
                                     using LiteBus.Commands.Abstractions;
                                     using LiteBus.Messaging.Abstractions;

                                     [HandlerTag("orphan")]
                                     public sealed class {|#0:OrphanTaggedHandler|} : ICommandHandler<SampleCommand>
                                     {
                                         public Task HandleAsync(SampleCommand message, CancellationToken cancellationToken = default) => Task.CompletedTask;
                                     }

                                     public sealed record SampleCommand : ICommand;
                                     """;

        var compilation = await AnalyzerTest.CreateCompilationAsync(
            [handlerSource, senderSource]).ConfigureAwait(false);

        var perFile = await AnalyzerTest.GetPerFileDiagnosticsAsync(
            compilation,
            0,
            new OrphanHandlerTagAnalyzer()).ConfigureAwait(false);

        Assert.DoesNotContain(perFile, diagnostic => diagnostic.Id == DiagnosticIds.OrphanHandlerTag);

        await AnalyzerTest.VerifyMultiFileAsync<OrphanHandlerTagAnalyzer>(
            [handlerMarkup, senderSource],
            (DiagnosticDescriptors.OrphanHandlerTag, 0, new object[] { "OrphanTaggedHandler", "orphan" })).ConfigureAwait(false);
    }
}
