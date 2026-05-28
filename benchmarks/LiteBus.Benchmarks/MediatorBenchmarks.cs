using BenchmarkDotNet.Attributes;
using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Events;
using LiteBus.Events.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Registry;
using LiteBus.Queries;
using LiteBus.Queries.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Benchmarks;

[MemoryDiagnoser]
public class CommandBenchmarks
{
    private BenchmarkCommand _command = null!;
    private BenchmarkCommandHandler _directHandler = null!;
    private ICommandMediator _mediator = null!;

    [GlobalSetup]
    public void Setup()
    {
        MessageRegistryAccessor.Instance.Clear();

        var serviceProvider = new ServiceCollection()
            .AddLiteBus(configuration =>
            {
                configuration.AddCommandModule(builder =>
                {
                    builder.Register<BenchmarkCommand>();
                    builder.Register<BenchmarkCommandHandler>();
                });
            })
            .BuildServiceProvider();

        _command = new BenchmarkCommand();
        _directHandler = new BenchmarkCommandHandler();
        _mediator = serviceProvider.GetRequiredService<ICommandMediator>();
    }

    [Benchmark(Baseline = true)]
    public Task DirectCommandHandler()
    {
        return _directHandler.HandleAsync(_command);
    }

    [Benchmark]
    public Task SendCommand()
    {
        return _mediator.SendAsync(_command);
    }
}

[MemoryDiagnoser]
public class QueryBenchmarks
{
    private IQueryMediator _mediator = null!;
    private BenchmarkQuery _query = null!;

    [GlobalSetup]
    public void Setup()
    {
        MessageRegistryAccessor.Instance.Clear();

        var serviceProvider = new ServiceCollection()
            .AddLiteBus(configuration =>
            {
                configuration.AddQueryModule(builder =>
                {
                    builder.Register<BenchmarkQuery>();
                    builder.Register<BenchmarkQueryHandler>();
                });
            })
            .BuildServiceProvider();

        _mediator = serviceProvider.GetRequiredService<IQueryMediator>();
        _query = new BenchmarkQuery();
    }

    [Benchmark]
    public Task<int> SendQuery()
    {
        return _mediator.QueryAsync(_query);
    }
}

[MemoryDiagnoser]
public class EventBenchmarks
{
    private IEventPublisher _publisher = null!;
    private EventMediationSettings _settings = null!;

    [Params(1, 5, 20)]
    public int HandlerCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        MessageRegistryAccessor.Instance.Clear();

        var serviceProvider = new ServiceCollection()
            .AddLiteBus(configuration =>
            {
                configuration.AddEventModule(builder =>
                {
                    builder.Register<BenchmarkEvent>();
                    builder.Register<BenchmarkEventHandler01>();
                    builder.Register<BenchmarkEventHandler02>();
                    builder.Register<BenchmarkEventHandler03>();
                    builder.Register<BenchmarkEventHandler04>();
                    builder.Register<BenchmarkEventHandler05>();
                    builder.Register<BenchmarkEventHandler06>();
                    builder.Register<BenchmarkEventHandler07>();
                    builder.Register<BenchmarkEventHandler08>();
                    builder.Register<BenchmarkEventHandler09>();
                    builder.Register<BenchmarkEventHandler10>();
                    builder.Register<BenchmarkEventHandler11>();
                    builder.Register<BenchmarkEventHandler12>();
                    builder.Register<BenchmarkEventHandler13>();
                    builder.Register<BenchmarkEventHandler14>();
                    builder.Register<BenchmarkEventHandler15>();
                    builder.Register<BenchmarkEventHandler16>();
                    builder.Register<BenchmarkEventHandler17>();
                    builder.Register<BenchmarkEventHandler18>();
                    builder.Register<BenchmarkEventHandler19>();
                    builder.Register<BenchmarkEventHandler20>();
                });
            })
            .BuildServiceProvider();

        _publisher = serviceProvider.GetRequiredService<IEventPublisher>();
        _settings = new EventMediationSettings
        {
            Routing = new EventMediationRoutingSettings
            {
                HandlerPredicate = descriptor => EventHandlerIndexes.GetIndex(descriptor.HandlerType) <= HandlerCount
            }
        };
    }

    [Benchmark]
    public Task PublishEvent()
    {
        return _publisher.PublishAsync(new BenchmarkEvent(), _settings);
    }
}

[MemoryDiagnoser]
public class OpenGenericHandlerBenchmarks
{
    private BenchmarkOpenGenericCommand _command = null!;
    private ICommandMediator _mediator = null!;

    [GlobalSetup]
    public void Setup()
    {
        MessageRegistryAccessor.Instance.Clear();

        var serviceProvider = new ServiceCollection()
            .AddLiteBus(configuration =>
            {
                configuration.AddCommandModule(builder =>
                {
                    builder.Register(typeof(BenchmarkOpenGenericPreHandler<>));
                    builder.Register<BenchmarkOpenGenericCommand>();
                    builder.Register<BenchmarkOpenGenericCommandHandler>();
                });
            })
            .BuildServiceProvider();

        _command = new BenchmarkOpenGenericCommand();
        _mediator = serviceProvider.GetRequiredService<ICommandMediator>();
    }

    [Benchmark]
    public Task SendCommandWithOpenGenericPreHandler()
    {
        return _mediator.SendAsync(_command);
    }
}

[MemoryDiagnoser]
public class TagFilteringBenchmarks
{
    private TaggedBenchmarkEvent _event = null!;
    private IEventPublisher _publisher = null!;
    private EventMediationSettings _settings = null!;

    [GlobalSetup]
    public void Setup()
    {
        MessageRegistryAccessor.Instance.Clear();

        var serviceProvider = new ServiceCollection()
            .AddLiteBus(configuration =>
            {
                configuration.AddEventModule(builder =>
                {
                    builder.Register<TaggedBenchmarkEvent>();
                    builder.Register<TaggedBenchmarkEventHandler>();
                    builder.Register<UntaggedBenchmarkEventHandler>();
                });
            })
            .BuildServiceProvider();

        _event = new TaggedBenchmarkEvent();
        _publisher = serviceProvider.GetRequiredService<IEventPublisher>();
        _settings = new EventMediationSettings
        {
            Routing = new EventMediationRoutingSettings
            {
                Tags = ["included"]
            }
        };
    }

    [Benchmark]
    public Task PublishWithTagFilter()
    {
        return _publisher.PublishAsync(_event, _settings);
    }
}

public sealed class BenchmarkCommand : ICommand;

public sealed class BenchmarkCommandHandler : ICommandHandler<BenchmarkCommand>
{
    public Task HandleAsync(BenchmarkCommand message, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

public sealed class BenchmarkQuery : IQuery<int>;

public sealed class BenchmarkQueryHandler : IQueryHandler<BenchmarkQuery, int>
{
    public Task<int> HandleAsync(BenchmarkQuery message, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(42);
    }
}

public sealed class BenchmarkOpenGenericCommand : ICommand;

public sealed class BenchmarkOpenGenericCommandHandler : ICommandHandler<BenchmarkOpenGenericCommand>
{
    public Task HandleAsync(BenchmarkOpenGenericCommand message, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

public sealed class BenchmarkOpenGenericPreHandler<TCommand> : ICommandPreHandler<TCommand>
    where TCommand : ICommand
{
    public Task PreHandleAsync(TCommand message, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

public sealed class BenchmarkEvent : IEvent
{
    public int Count { get; private set; }

    public void Increment()
    {
        Count++;
    }
}

public sealed class TaggedBenchmarkEvent : IEvent;

[HandlerTag("included")]
public sealed class TaggedBenchmarkEventHandler : IEventHandler<TaggedBenchmarkEvent>
{
    public Task HandleAsync(TaggedBenchmarkEvent message, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

public sealed class UntaggedBenchmarkEventHandler : IEventHandler<TaggedBenchmarkEvent>
{
    public Task HandleAsync(TaggedBenchmarkEvent message, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

public static class EventHandlerIndexes
{
    private static readonly IReadOnlyDictionary<Type, int> Indexes = new Dictionary<Type, int>
    {
        [typeof(BenchmarkEventHandler01)] = 1,
        [typeof(BenchmarkEventHandler02)] = 2,
        [typeof(BenchmarkEventHandler03)] = 3,
        [typeof(BenchmarkEventHandler04)] = 4,
        [typeof(BenchmarkEventHandler05)] = 5,
        [typeof(BenchmarkEventHandler06)] = 6,
        [typeof(BenchmarkEventHandler07)] = 7,
        [typeof(BenchmarkEventHandler08)] = 8,
        [typeof(BenchmarkEventHandler09)] = 9,
        [typeof(BenchmarkEventHandler10)] = 10,
        [typeof(BenchmarkEventHandler11)] = 11,
        [typeof(BenchmarkEventHandler12)] = 12,
        [typeof(BenchmarkEventHandler13)] = 13,
        [typeof(BenchmarkEventHandler14)] = 14,
        [typeof(BenchmarkEventHandler15)] = 15,
        [typeof(BenchmarkEventHandler16)] = 16,
        [typeof(BenchmarkEventHandler17)] = 17,
        [typeof(BenchmarkEventHandler18)] = 18,
        [typeof(BenchmarkEventHandler19)] = 19,
        [typeof(BenchmarkEventHandler20)] = 20
    };

    public static int GetIndex(Type handlerType)
    {
        return Indexes.TryGetValue(handlerType, out var index) ? index : int.MaxValue;
    }
}

public sealed class BenchmarkEventHandler01 : BenchmarkEventHandlerBase;
public sealed class BenchmarkEventHandler02 : BenchmarkEventHandlerBase;
public sealed class BenchmarkEventHandler03 : BenchmarkEventHandlerBase;
public sealed class BenchmarkEventHandler04 : BenchmarkEventHandlerBase;
public sealed class BenchmarkEventHandler05 : BenchmarkEventHandlerBase;
public sealed class BenchmarkEventHandler06 : BenchmarkEventHandlerBase;
public sealed class BenchmarkEventHandler07 : BenchmarkEventHandlerBase;
public sealed class BenchmarkEventHandler08 : BenchmarkEventHandlerBase;
public sealed class BenchmarkEventHandler09 : BenchmarkEventHandlerBase;
public sealed class BenchmarkEventHandler10 : BenchmarkEventHandlerBase;
public sealed class BenchmarkEventHandler11 : BenchmarkEventHandlerBase;
public sealed class BenchmarkEventHandler12 : BenchmarkEventHandlerBase;
public sealed class BenchmarkEventHandler13 : BenchmarkEventHandlerBase;
public sealed class BenchmarkEventHandler14 : BenchmarkEventHandlerBase;
public sealed class BenchmarkEventHandler15 : BenchmarkEventHandlerBase;
public sealed class BenchmarkEventHandler16 : BenchmarkEventHandlerBase;
public sealed class BenchmarkEventHandler17 : BenchmarkEventHandlerBase;
public sealed class BenchmarkEventHandler18 : BenchmarkEventHandlerBase;
public sealed class BenchmarkEventHandler19 : BenchmarkEventHandlerBase;
public sealed class BenchmarkEventHandler20 : BenchmarkEventHandlerBase;

public abstract class BenchmarkEventHandlerBase : IEventHandler<BenchmarkEvent>
{
    public Task HandleAsync(BenchmarkEvent message, CancellationToken cancellationToken = default)
    {
        message.Increment();
        return Task.CompletedTask;
    }
}
