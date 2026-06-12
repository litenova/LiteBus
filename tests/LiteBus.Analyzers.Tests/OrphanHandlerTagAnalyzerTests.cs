namespace LiteBus.Analyzers.Tests;

public sealed class OrphanHandlerTagAnalyzerTests
{
    [Fact]
    public async Task OrphanHandlerTag_ShouldReportWhenTagIsNotReferenced()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Messaging.Abstractions;

                              [HandlerTag("orphan")]
                              public sealed class {|#2:OrphanTaggedHandler|} : ICommandHandler<SampleCommand>
                              {
                                  public Task HandleAsync(SampleCommand message, CancellationToken cancellationToken = default) => Task.CompletedTask;
                              }

                              public sealed record SampleCommand : ICommand;
                              """;

        await AnalyzerTest.VerifyDiagnosticAsync<OrphanHandlerTagAnalyzer>(
            source,
            DiagnosticDescriptors.OrphanHandlerTag,
            2,
            "OrphanTaggedHandler",
            "orphan");
    }

    [Fact]
    public async Task OrphanHandlerTag_ShouldNotReportWhenTagIsReferenced()
    {
        const string source = """
                              using System.Collections.Generic;
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Messaging.Abstractions;

                              [HandlerTag("frontend")]
                              public sealed class TaggedHandler : ICommandHandler<SampleCommand>
                              {
                                  public Task HandleAsync(SampleCommand message, CancellationToken cancellationToken = default) => Task.CompletedTask;
                              }

                              public sealed class Sender
                              {
                                  public void Send(ICommandMediator mediator)
                                  {
                                      var settings = new CommandMediationSettings();
                                      settings.Filters.Tags = new List<string> { "frontend" };
                                  }
                              }

                              public sealed record SampleCommand : ICommand;
                              """;

        await AnalyzerTest.VerifyNoDiagnosticsAsync<OrphanHandlerTagAnalyzer>(source);
    }

    [Fact]
    public async Task OrphanHandlerTag_ShouldNotReportWhenEventRoutingTagsAreReferenced()
    {
        const string source = """
                              using System.Collections.Generic;
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Events.Abstractions;
                              using LiteBus.Messaging.Abstractions;

                              [HandlerTag("critical")]
                              public sealed class TaggedEventHandler : IEventHandler<SampleEvent>
                              {
                                  public Task HandleAsync(SampleEvent message, CancellationToken cancellationToken = default) => Task.CompletedTask;
                              }

                              public sealed class Publisher
                              {
                                  public void Publish(IEventMediator mediator)
                                  {
                                      _ = new EventMediationSettings
                                      {
                                          Routing = new EventRoutingSettings { Tags = new List<string> { "critical" } }
                                      };
                                  }
                              }

                              public sealed record SampleEvent : IEvent;
                              """;

        await AnalyzerTest.VerifyNoDiagnosticsAsync<OrphanHandlerTagAnalyzer>(source);
    }
}