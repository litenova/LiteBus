using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Events;
using LiteBus.Events.Abstractions;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Queries;
using LiteBus.Queries.Abstractions;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Runtime.UnitTests.ApplicationModules;

public sealed class DuplicateMediatorModuleRegistrationTests
{
    [Fact]
    public void AddMessaging_Twice_ShouldApplyBothContracts()
    {
        var services = new ServiceCollection();

        services.AddLiteBus(builder =>
        {
            builder.AddMessaging(messaging => messaging.Contracts.Register<FirstContractMessage>("test.first", 1));
            builder.AddMessaging(messaging => messaging.Contracts.Register<SecondContractMessage>("test.second", 1));
        });

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IMessageContractRegistry>();

        registry.GetMessageType("test.first", 1).Should().Be<FirstContractMessage>();
        registry.GetMessageType("test.second", 1).Should().Be<SecondContractMessage>();
    }

    [Fact]
    public async Task AddCommands_Twice_ShouldMediateBothCommands()
    {
        var recorder = new DuplicateCommandRecorder();

        using var provider = new ServiceCollection()
            .AddSingleton(recorder)
            .AddLiteBus(builder =>
            {
                builder.AddMessaging(_ =>
                {
                });

                builder.AddCommands(commands => commands
                    .Register<FirstDuplicateCommand>()
                    .Register<FirstDuplicateCommandHandler>());

                builder.AddCommands(commands => commands
                    .Register<SecondDuplicateCommand>()
                    .Register<SecondDuplicateCommandHandler>());
            })
            .BuildServiceProvider();

        var mediator = provider.GetRequiredService<ICommandMediator>();

        await mediator.SendAsync(new FirstDuplicateCommand()).ConfigureAwait(false);
        await mediator.SendAsync(new SecondDuplicateCommand()).ConfigureAwait(false);

        recorder.Handled.Should().Equal("first", "second");
    }

    [Fact]
    public async Task AddQueries_Twice_ShouldMediateBothQueries()
    {
        using var provider = new ServiceCollection()
            .AddLiteBus(builder =>
            {
                builder.AddMessaging(_ =>
                {
                });

                builder.AddQueries(queries => queries
                    .Register<FirstDuplicateQuery>()
                    .Register<FirstDuplicateQueryHandler>());

                builder.AddQueries(queries => queries
                    .Register<SecondDuplicateQuery>()
                    .Register<SecondDuplicateQueryHandler>());
            })
            .BuildServiceProvider();

        var mediator = provider.GetRequiredService<IQueryMediator>();
        var first = await mediator.QueryAsync(new FirstDuplicateQuery()).ConfigureAwait(false);
        var second = await mediator.QueryAsync(new SecondDuplicateQuery()).ConfigureAwait(false);

        first.Should().Be("first");
        second.Should().Be("second");
    }

    [Fact]
    public async Task AddEvents_Twice_ShouldHandleBothEvents()
    {
        var recorder = new DuplicateEventRecorder();

        using var provider = new ServiceCollection()
            .AddSingleton(recorder)
            .AddLiteBus(builder =>
            {
                builder.AddMessaging(_ =>
                {
                });

                builder.AddEvents(events => events
                    .Register<FirstDuplicateEvent>()
                    .Register<FirstDuplicateEventHandler>());

                builder.AddEvents(events => events
                    .Register<SecondDuplicateEvent>()
                    .Register<SecondDuplicateEventHandler>());
            })
            .BuildServiceProvider();

        var mediator = provider.GetRequiredService<IEventMediator>();

        await mediator.PublishAsync(new FirstDuplicateEvent()).ConfigureAwait(false);
        await mediator.PublishAsync(new SecondDuplicateEvent()).ConfigureAwait(false);

        recorder.Handled.Should().Equal("first", "second");
    }

    [Fact]
    public void AddCommandModule_Twice_ShouldConfigureOneModule()
    {
        var registry = new ModuleRegistry();
        registry.AddMessageModule(_ =>
        {
        });
        registry.AddCommandModule(commands => commands.Register<FirstDuplicateCommand>());
        registry.AddCommandModule(commands => commands.Register<SecondDuplicateCommand>());

        registry.Find<CommandModule>().Should().NotBeNull();
        registry.BuildOrder().Count(descriptor => descriptor.ModuleType == typeof(CommandModule)).Should().Be(1);
    }

    [Fact]
    public void AddCommandModule_SecondCallWithChild_ShouldRegisterChild()
    {
        var registry = new ModuleRegistry();
        registry.AddMessageModule(_ =>
        {
        });
        registry.AddCommandModule(commands => commands.Register<FirstDuplicateCommand>());
        registry.AddCommandModule(commands => commands.RegisterChildModule(new DuplicateChildModule()));

        registry.BuildOrder().Select(descriptor => descriptor.ModuleType).Should().Contain(typeof(DuplicateChildModule));
    }

    private sealed record FirstContractMessage;

    private sealed record SecondContractMessage;

    private sealed record FirstDuplicateCommand : ICommand;

    private sealed record SecondDuplicateCommand : ICommand;

    private sealed class DuplicateCommandRecorder
    {
        public List<string> Handled { get; } = [];
    }

    private sealed class FirstDuplicateCommandHandler : ICommandHandler<FirstDuplicateCommand>
    {
        private readonly DuplicateCommandRecorder _recorder;

        public FirstDuplicateCommandHandler(DuplicateCommandRecorder recorder)
        {
            _recorder = recorder;
        }

        public Task HandleAsync(FirstDuplicateCommand message, CancellationToken cancellationToken = default)
        {
            _recorder.Handled.Add("first");
            return Task.CompletedTask;
        }
    }

    private sealed class SecondDuplicateCommandHandler : ICommandHandler<SecondDuplicateCommand>
    {
        private readonly DuplicateCommandRecorder _recorder;

        public SecondDuplicateCommandHandler(DuplicateCommandRecorder recorder)
        {
            _recorder = recorder;
        }

        public Task HandleAsync(SecondDuplicateCommand message, CancellationToken cancellationToken = default)
        {
            _recorder.Handled.Add("second");
            return Task.CompletedTask;
        }
    }

    private sealed record FirstDuplicateQuery : IQuery<string>;

    private sealed record SecondDuplicateQuery : IQuery<string>;

    private sealed class FirstDuplicateQueryHandler : IQueryHandler<FirstDuplicateQuery, string>
    {
        public Task<string> HandleAsync(FirstDuplicateQuery message, CancellationToken cancellationToken = default)
        {
            return Task.FromResult("first");
        }
    }

    private sealed class SecondDuplicateQueryHandler : IQueryHandler<SecondDuplicateQuery, string>
    {
        public Task<string> HandleAsync(SecondDuplicateQuery message, CancellationToken cancellationToken = default)
        {
            return Task.FromResult("second");
        }
    }

    private sealed record FirstDuplicateEvent : IEvent;

    private sealed record SecondDuplicateEvent : IEvent;

    private sealed class DuplicateEventRecorder
    {
        public List<string> Handled { get; } = [];
    }

    private sealed class FirstDuplicateEventHandler : IEventHandler<FirstDuplicateEvent>
    {
        private readonly DuplicateEventRecorder _recorder;

        public FirstDuplicateEventHandler(DuplicateEventRecorder recorder)
        {
            _recorder = recorder;
        }

        public Task HandleAsync(FirstDuplicateEvent message, CancellationToken cancellationToken = default)
        {
            _recorder.Handled.Add("first");
            return Task.CompletedTask;
        }
    }

    private sealed class SecondDuplicateEventHandler : IEventHandler<SecondDuplicateEvent>
    {
        private readonly DuplicateEventRecorder _recorder;

        public SecondDuplicateEventHandler(DuplicateEventRecorder recorder)
        {
            _recorder = recorder;
        }

        public Task HandleAsync(SecondDuplicateEvent message, CancellationToken cancellationToken = default)
        {
            _recorder.Handled.Add("second");
            return Task.CompletedTask;
        }
    }

    private sealed class DuplicateChildModule : IModule
    {
        public void Build(IModuleConfiguration configuration)
        {
        }
    }
}
