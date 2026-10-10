namespace LiteBus.Analyzers.UnitTests;

/// <summary>
///     Regression tests for the <see cref="OrphanHandlerTagAnalyzer" /> rule.
/// </summary>
public sealed class OrphanHandlerTagAnalyzerRegressionTests
{
    /// <summary>
    ///     Verifies that a tag referenced through a constant is not reported as orphaned.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task OrphanHandlerTag_ShouldNotReportWhenTagIsReferencedThroughConstant()
    {
        const string source = """
                              using System.Collections.Generic;
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Messaging.Abstractions;

                              public static class Tags
                              {
                                  public const string Frontend = "frontend";
                              }

                              [HandlerTag(Tags.Frontend)]
                              public sealed class TaggedHandler : ICommandHandler<SampleCommand>
                              {
                                  public Task HandleAsync(SampleCommand message, CancellationToken cancellationToken = default) => Task.CompletedTask;
                              }

                              public sealed class Sender
                              {
                                  public void Send(ICommandMediator mediator)
                                  {
                                      var settings = new CommandMediationSettings
                                      {
                                          Routing = new CommandRoutingSettings
                                          {
                                              Tags = new List<string> { Tags.Frontend }
                                          }
                                      };
                                  }
                              }

                              public sealed record SampleCommand : ICommand;
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<OrphanHandlerTagAnalyzer>(source);
    }

    /// <summary>
    ///     Verifies that a non-tag string literal in a mediator call does not suppress an orphan-tag report.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task OrphanHandlerTag_ShouldReportWhenOnlyAnUnrelatedLiteralMatches()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Messaging.Abstractions;

                              [HandlerTag("orphan")]
                              public sealed class {|#0:OrphanTaggedHandler|} : ICommandHandler<SampleCommand>
                              {
                                  public Task HandleAsync(SampleCommand message, CancellationToken cancellationToken = default) => Task.CompletedTask;
                              }

                              public sealed class Sender
                              {
                                  public void Send(ICommandMediator mediator)
                                  {
                                      mediator.SendAsync(new SampleCommand("orphan"));
                                  }
                              }

                              public sealed record SampleCommand(string Reason) : ICommand;
                              """;

        return AnalyzerTest.VerifyDiagnosticAsync<OrphanHandlerTagAnalyzer>(
            source,
            DiagnosticDescriptors.OrphanHandlerTag,
            0,
            "OrphanTaggedHandler",
            "orphan");
    }

    /// <summary>
    ///     Verifies that each unreferenced tag in a <c>HandlerTags</c> array is reported.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task OrphanHandlerTag_ShouldReportEachUnreferencedArrayTag()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Messaging.Abstractions;

                              [HandlerTags("orphan-a", "orphan-b")]
                              public sealed class {|#0:OrphanTaggedHandler|} : ICommandHandler<SampleCommand>
                              {
                                  public Task HandleAsync(SampleCommand message, CancellationToken cancellationToken = default) => Task.CompletedTask;
                              }

                              public sealed record SampleCommand : ICommand;
                              """;

        return AnalyzerTest.VerifyDiagnosticsAsync<OrphanHandlerTagAnalyzer>(
            source,
            (DiagnosticDescriptors.OrphanHandlerTag, 0, new object[] { "OrphanTaggedHandler", "orphan-a" }),
            (DiagnosticDescriptors.OrphanHandlerTag, 0, new object[] { "OrphanTaggedHandler", "orphan-b" }));
    }
}
