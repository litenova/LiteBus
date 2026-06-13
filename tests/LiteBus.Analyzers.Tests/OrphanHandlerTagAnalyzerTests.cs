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
            "orphan").ConfigureAwait(false);
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
                                      var settings = new CommandMediationSettings
                                      {
                                          Routing = new CommandRoutingSettings
                                          {
                                              Tags = new List<string> { "frontend" }
                                          }
                                      };
                                  }
                              }

                              public sealed record SampleCommand : ICommand;
                              """;

        await AnalyzerTest.VerifyNoDiagnosticsAsync<OrphanHandlerTagAnalyzer>(source).ConfigureAwait(false);
    }

    [Fact]
    public async Task OrphanHandlerTag_ShouldNotReportWhenQueryFilterTagsAreReferenced()
    {
        const string source = """
                              using System.Collections.Generic;
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Messaging.Abstractions;
                              using LiteBus.Queries.Abstractions;

                              [HandlerTag("reporting")]
                              public sealed class TaggedQueryHandler : IQueryHandler<SampleQuery, string>
                              {
                                  public Task<string> HandleAsync(SampleQuery query, CancellationToken cancellationToken = default)
                                      => Task.FromResult("ok");
                              }

                              public sealed class QueryRunner
                              {
                                  public void Run(IQueryMediator mediator)
                                  {
                                      var settings = new QueryMediationSettings
                                      {
                                          Routing = new QueryRoutingSettings
                                          {
                                              Tags = new List<string> { "reporting" }
                                          }
                                      };
                                  }
                              }

                              public sealed record SampleQuery : IQuery<string>;
                              """;

        await AnalyzerTest.VerifyNoDiagnosticsAsync<OrphanHandlerTagAnalyzer>(source).ConfigureAwait(false);
    }

    [Fact]
    public async Task OrphanHandlerTag_ShouldNotReportWhenExtensionMethodTagIsReferenced()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Messaging.Abstractions;

                              [HandlerTag("billing")]
                              public sealed class TaggedCommandHandler : ICommandHandler<SampleCommand>
                              {
                                  public Task HandleAsync(SampleCommand message, CancellationToken cancellationToken = default) => Task.CompletedTask;
                              }

                              public static class CommandMediatorExtensions
                              {
                                  public static Task SendAsync(this ICommandMediator commandMediator, ICommand command, string tag, CancellationToken cancellationToken = default)
                                      => commandMediator.SendAsync(command, new CommandMediationSettings { Routing = new CommandRoutingSettings { Tags = [tag] } }, cancellationToken);
                              }

                              public sealed class Sender
                              {
                                  public void Send(ICommandMediator mediator)
                                  {
                                      mediator.SendAsync(new SampleCommand(), "billing");
                                  }
                              }

                              public sealed record SampleCommand : ICommand;
                              """;

        await AnalyzerTest.VerifyNoDiagnosticsAsync<OrphanHandlerTagAnalyzer>(source).ConfigureAwait(false);
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

        await AnalyzerTest.VerifyNoDiagnosticsAsync<OrphanHandlerTagAnalyzer>(source).ConfigureAwait(false);
    }
}