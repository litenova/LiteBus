using System.Reflection;
using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Runtime;
using LiteBus.Runtime.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Extensions.UnitTests.Composition.ApplicationModules;

public sealed class ApplicationModuleCompositionTests
{
    [Fact]
    public async Task TwoApplicationModules_EachAddingCommands_ShouldMediateBoth()
    {
        var recorder = new AppCompositionRecorder();

        using var provider = new ServiceCollection()
            .AddSingleton(recorder)
            .AddLiteBus(builder =>
            {
                builder.AddMessaging(_ =>
                {
                });
                builder.AddApplicationModule<FirstCompositionModule>();
                builder.AddApplicationModule<SecondCompositionModule>();
            })
            .BuildServiceProvider();

        var mediator = provider.GetRequiredService<ICommandMediator>();

        await mediator.SendAsync(new FirstAppCommand()).ConfigureAwait(false);
        await mediator.SendAsync(new SecondAppCommand()).ConfigureAwait(false);

        recorder.Handled.Should().Equal("first", "second");
    }

    [Fact]
    public async Task ApplicationModule_WithDirectRegistration_ShouldMediateBoth()
    {
        var recorder = new AppCompositionRecorder();

        using var provider = new ServiceCollection()
            .AddSingleton(recorder)
            .AddLiteBus(builder =>
            {
                builder.AddMessaging(_ =>
                {
                });
                builder.AddCommands(commands => commands
                    .Register<FirstAppCommand>()
                    .Register<FirstAppCommandHandler>());
                builder.AddApplicationModule<SecondCompositionModule>();
            })
            .BuildServiceProvider();

        var mediator = provider.GetRequiredService<ICommandMediator>();

        await mediator.SendAsync(new FirstAppCommand()).ConfigureAwait(false);
        await mediator.SendAsync(new SecondAppCommand()).ConfigureAwait(false);

        recorder.Handled.Should().Equal("first", "second");
    }

    [Fact]
    public async Task AddApplicationModulesFromAssembly_ShouldMediateDiscoveredModules()
    {
        var recorder = new AppCompositionRecorder();
        var assembly = new FakeCompositionAssembly(typeof(SecondCompositionModule), typeof(FirstCompositionModule));

        using var provider = new ServiceCollection()
            .AddSingleton(recorder)
            .AddLiteBus(builder =>
            {
                builder.AddMessaging(_ =>
                {
                });
                builder.AddApplicationModulesFromAssembly(assembly);
            })
            .BuildServiceProvider();

        var mediator = provider.GetRequiredService<ICommandMediator>();

        await mediator.SendAsync(new FirstAppCommand()).ConfigureAwait(false);
        await mediator.SendAsync(new SecondAppCommand()).ConfigureAwait(false);

        recorder.Handled.Should().Equal("first", "second");
    }

    private sealed class FakeCompositionAssembly : Assembly
    {
        private readonly Type[] _types;

        public FakeCompositionAssembly(params Type[] types)
        {
            _types = types;
        }

        public override Type[] GetTypes()
        {
            return _types;
        }
    }
}

internal sealed class AppCompositionRecorder
{
    public List<string> Handled { get; } = [];
}

internal sealed record FirstAppCommand : ICommand;

internal sealed record SecondAppCommand : ICommand;

internal sealed class FirstAppCommandHandler : ICommandHandler<FirstAppCommand>
{
    private readonly AppCompositionRecorder _recorder;

    public FirstAppCommandHandler(AppCompositionRecorder recorder)
    {
        _recorder = recorder;
    }

    public Task HandleAsync(FirstAppCommand message, CancellationToken cancellationToken = default)
    {
        _recorder.Handled.Add("first");
        return Task.CompletedTask;
    }
}

internal sealed class SecondAppCommandHandler : ICommandHandler<SecondAppCommand>
{
    private readonly AppCompositionRecorder _recorder;

    public SecondAppCommandHandler(AppCompositionRecorder recorder)
    {
        _recorder = recorder;
    }

    public Task HandleAsync(SecondAppCommand message, CancellationToken cancellationToken = default)
    {
        _recorder.Handled.Add("second");
        return Task.CompletedTask;
    }
}

internal sealed class FirstCompositionModule : IApplicationModule
{
    public void Configure(ILiteBusBuilder liteBus)
    {
        liteBus.AddCommands(commands => commands
            .Register<FirstAppCommand>()
            .Register<FirstAppCommandHandler>());
    }
}

internal sealed class SecondCompositionModule : IApplicationModule
{
    public void Configure(ILiteBusBuilder liteBus)
    {
        liteBus.AddCommands(commands => commands
            .Register<SecondAppCommand>()
            .Register<SecondAppCommandHandler>());
    }
}
