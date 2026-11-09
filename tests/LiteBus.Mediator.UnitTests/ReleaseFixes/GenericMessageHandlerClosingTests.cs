using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Events;
using LiteBus.Events.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Queries;
using LiteBus.Queries.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.ReleaseFixes.GenericHandlerClosing;

/// <summary>
///     Verifies that a handler registered for a generic message is closed by matching the message type in its contract
///     against the runtime message, wherever its type parameters sit in that type (#169).
/// </summary>
/// <remarks>
///     Each test registers the handler by its open definition and sends a closed message, the way an application does.
///     The first two tests are the reproduction from #169.
/// </remarks>
[Collection("Sequential")]
public sealed class GenericMessageHandlerClosingTests : LiteBusTestBase
{
    [Fact]
    public async Task A_query_whose_result_nests_the_type_parameter_reaches_its_handler()
    {
        var provider = BuildProvider(queries: builder => builder.Register(typeof(ListQueryHandler<>)));
        var mediator = provider.GetRequiredService<IQueryMediator>();

        List<string> strings = await mediator.QueryAsync(new ListQuery<string>("value")).ConfigureAwait(false);
        List<int> numbers = await mediator.QueryAsync(new ListQuery<int>(7)).ConfigureAwait(false);

        strings.Should().Equal("value");
        numbers.Should().Equal(7);
    }

    [Fact]
    public async Task A_command_with_two_type_parameters_reaches_its_handler()
    {
        var provider = BuildProvider(commands: builder => builder.Register(typeof(CreateEntityCommandHandler<,>)));
        var mediator = provider.GetRequiredService<ICommandMediator>();
        var customer = new Customer(Guid.NewGuid());

        var customerId = await mediator.SendAsync(new CreateEntityCommand<Customer, Guid> { EntityData = customer })
            .ConfigureAwait(false);
        var productId = await mediator.SendAsync(new CreateEntityCommand<Product, int> { EntityData = new Product(42) })
            .ConfigureAwait(false);

        customerId.Should().Be(customer.Id);
        productId.Should().Be(42);
    }

    [Fact]
    public async Task A_handler_declaring_its_type_parameters_in_another_order_than_the_message_reaches_it()
    {
        var provider = BuildProvider(commands: builder => builder.Register(typeof(RenameEntityCommandHandler<,>)));

        // The handler is RenameEntityCommandHandler<TKey, TEntity> for RenameEntityCommand<TEntity, TKey>, so closing
        // it with the message's arguments in their own order would put Customer where TKey belongs.
        var name = await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new RenameEntityCommand<Customer, Guid>(new Customer(Guid.Empty), "renamed"))
            .ConfigureAwait(false);

        name.Should().Be("Customer:Guid:renamed");
    }

    [Fact]
    public async Task Handlers_are_chosen_by_the_shape_of_the_message_type_argument()
    {
        var provider = BuildProvider(queries: builder =>
        {
            builder.Register(typeof(ListEnvelopeQueryHandler<>));
            builder.Register(typeof(ArrayEnvelopeQueryHandler<>));
        });
        var mediator = provider.GetRequiredService<IQueryMediator>();

        // Both handlers are registered for EnvelopeQuery<>, and each fits one shape of its argument: List<T> or T[].
        // The handler's type parameter sits inside the message's type argument rather than being that argument.
        var list = await mediator.QueryAsync(new EnvelopeQuery<List<int>>([1, 2])).ConfigureAwait(false);
        var array = await mediator.QueryAsync(new EnvelopeQuery<string[]>(["a"])).ConfigureAwait(false);

        list.Should().Be("list:Int32:2");
        array.Should().Be("array:String:1");
    }

    [Fact]
    public async Task Constraints_choose_between_handlers_of_one_generic_message()
    {
        var provider = BuildProvider(queries: builder =>
        {
            builder.Register(typeof(ReferenceDescribeQueryHandler<>));
            builder.Register(typeof(ValueDescribeQueryHandler<>));
        });
        var mediator = provider.GetRequiredService<IQueryMediator>();

        var reference = await mediator.QueryAsync(new DescribeQuery<string>()).ConfigureAwait(false);
        var value = await mediator.QueryAsync(new DescribeQuery<int>()).ConfigureAwait(false);

        reference.Should().Be("reference:String");
        value.Should().Be("value:Int32");
    }

    [Fact]
    public async Task A_stream_query_whose_items_nest_the_type_parameter_streams_through_its_handler()
    {
        var provider = BuildProvider(queries: builder => builder.Register(typeof(PagedStreamQueryHandler<>)));
        var pages = new List<Page<string>>();

        await foreach (var page in provider.GetRequiredService<IQueryMediator>()
                           .StreamAsync(new PagedStreamQuery<string>("item", 3))
                           .ConfigureAwait(false))
        {
            pages.Add(page);
        }

        pages.Select(page => page.Number).Should().Equal(1, 2, 3);
        pages.Should().OnlyContain(page => page.Item == "item");
    }

    [Fact]
    public async Task Pre_post_and_completion_handlers_of_a_generic_message_run_through_their_typed_contracts()
    {
        var recorder = new GenericClosingRecorder();
        var provider = BuildProvider(
            recorder,
            commands: builder =>
            {
                builder.Register(typeof(CreateEntityCommandHandler<,>));
                builder.Register(typeof(CreateEntityCommandPreHandler<,>));
                builder.Register(typeof(CreateEntityCommandPostHandler<,>));
                builder.Register(typeof(CreateEntityCommandCompletionHandler<,>));
            },
            queries: builder =>
            {
                builder.Register(typeof(ListQueryHandler<>));
                builder.Register(typeof(ListQueryPostHandler<>));
            });

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new CreateEntityCommand<Product, int> { EntityData = new Product(5) })
            .ConfigureAwait(false);
        await provider.GetRequiredService<IQueryMediator>()
            .QueryAsync(new ListQuery<string>("value"))
            .ConfigureAwait(false);

        recorder.Entries.Should().Equal(
            "pre:Product",
            "post:Product:5",
            "completion:Product:5",
            "post:List<String>:1");
    }

    [Fact]
    public async Task An_error_handler_of_a_generic_message_is_closed_from_the_message_and_can_recover_it()
    {
        var provider = BuildProvider(commands: builder =>
        {
            builder.Register(typeof(RejectEntityCommandHandler<,>));
            builder.Register(typeof(RejectEntityCommandErrorHandler<,>));
        });

        // The error handler declares its type parameters key first, so it is resolved only if it is closed by matching
        // the message rather than by the order of the message's own arguments.
        var id = await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new RejectEntityCommand<Product, int>(new Product(9)))
            .ConfigureAwait(false);

        id.Should().Be(9);
    }

    [Fact]
    public async Task A_handler_for_a_generic_interface_reaches_a_non_generic_event_that_implements_it()
    {
        var recorder = new GenericClosingRecorder();
        var provider = BuildProvider(
            recorder,
            events: builder =>
            {
                builder.Register<CustomerChangedEvent>();
                builder.Register(typeof(EntityChangedEventHandler<>));
            });

        await provider.GetRequiredService<IEventMediator>()
            .PublishAsync(new CustomerChangedEvent(new Customer(Guid.Empty)))
            .ConfigureAwait(false);

        recorder.Entries.Should().Equal("changed:Customer");
    }

    [Fact]
    public async Task A_message_implementing_the_handled_interface_twice_is_reported_as_ambiguous()
    {
        var provider = BuildProvider(events: builder =>
        {
            builder.Register<CustomerAndProductChangedEvent>();
            builder.Register(typeof(EntityChangedEventHandler<>));
        });

        var act = () => provider.GetRequiredService<IEventMediator>()
            .PublishAsync(new CustomerAndProductChangedEvent());

        (await act.Should().ThrowAsync<PipelineContractException>().ConfigureAwait(false))
            .Which.Message.Should().Contain("EntityChangedEventHandler").And.Contain("IEntityChanged");
    }

    [Fact]
    public async Task A_handler_type_parameter_the_message_cannot_bind_is_reported_as_a_contract_error()
    {
        var provider = BuildProvider(events: builder =>
        {
            builder.Register<NoticeEvent>();
            builder.Register(typeof(UnboundNoticeEventHandler<>));
        });

        var act = () => provider.GetRequiredService<IEventMediator>().PublishAsync(new NoticeEvent());

        (await act.Should().ThrowAsync<PipelineContractException>().ConfigureAwait(false))
            .Which.Message.Should().Contain("UnboundNoticeEventHandler").And.Contain("TExtra");
    }

    /// <summary>
    ///     Builds a provider with the messaging, command, query, and event modules.
    /// </summary>
    /// <param name="commands">Registers the command constructs under test.</param>
    /// <param name="queries">Registers the query constructs under test.</param>
    /// <param name="events">Registers the event constructs under test.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildProvider(
        Action<CommandModuleBuilder>? commands = null,
        Action<QueryModuleBuilder>? queries = null,
        Action<EventModuleBuilder>? events = null)
    {
        return BuildProvider(new GenericClosingRecorder(), commands, queries, events);
    }

    /// <summary>
    ///     Builds a provider with the messaging, command, query, and event modules and a shared recorder.
    /// </summary>
    /// <param name="recorder">The recorder the recording handlers write to.</param>
    /// <param name="commands">Registers the command constructs under test.</param>
    /// <param name="queries">Registers the query constructs under test.</param>
    /// <param name="events">Registers the event constructs under test.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildProvider(
        GenericClosingRecorder recorder,
        Action<CommandModuleBuilder>? commands = null,
        Action<QueryModuleBuilder>? queries = null,
        Action<EventModuleBuilder>? events = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(recorder);

        return services
            .AddLiteBus(liteBus =>
            {
                liteBus.AddMessaging(_ => { });
                liteBus.AddCommands(builder => commands?.Invoke(builder));
                liteBus.AddQueries(builder => queries?.Invoke(builder));
                liteBus.AddEvents(builder => events?.Invoke(builder));
            })
            .BuildServiceProvider();
    }
}

/// <summary>
///     Records what the recording handlers observed, in order.
/// </summary>
internal sealed class GenericClosingRecorder
{
    /// <summary>
    ///     Holds the recorded entries.
    /// </summary>
    private readonly ConcurrentQueue<string> _entries = new();

    /// <summary>
    ///     Gets the recorded entries in the order they were written.
    /// </summary>
    public IReadOnlyList<string> Entries => [.. _entries];

    /// <summary>
    ///     Records one entry.
    /// </summary>
    /// <param name="entry">The entry to record.</param>
    public void Record(string entry)
    {
        _entries.Enqueue(entry);
    }
}

/// <summary>
///     An entity with a key, as in the generic messages guide.
/// </summary>
/// <typeparam name="TKey">The key type.</typeparam>
internal interface IEntity<out TKey>
{
    /// <summary>
    ///     Gets the entity key.
    /// </summary>
    TKey Id { get; }
}

/// <summary>
///     An entity keyed by a <see cref="Guid" />.
/// </summary>
/// <param name="Id">The entity key.</param>
internal sealed record Customer(Guid Id) : IEntity<Guid>;

/// <summary>
///     An entity keyed by an <see cref="int" />.
/// </summary>
/// <param name="Id">The entity key.</param>
internal sealed record Product(int Id) : IEntity<int>;

/// <summary>
///     A query whose result type nests the message's type parameter, the first shape in #169.
/// </summary>
/// <typeparam name="T">The item type.</typeparam>
/// <param name="Item">The item the handler returns in a list.</param>
internal sealed record ListQuery<T>(T Item) : IQuery<List<T>>;

/// <summary>
///     Returns the query's item in a one-element list.
/// </summary>
/// <typeparam name="T">The item type.</typeparam>
internal sealed class ListQueryHandler<T> : IQueryHandler<ListQuery<T>, List<T>>
{
    /// <inheritdoc />
    public Task<List<T>> HandleAsync(ListQuery<T> message, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new List<T> { message.Item });
    }
}

/// <summary>
///     Records the typed result of a <see cref="ListQuery{T}" />.
/// </summary>
/// <typeparam name="T">The item type.</typeparam>
internal sealed class ListQueryPostHandler<T> : IQueryPostHandler<ListQuery<T>, List<T>>
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly GenericClosingRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ListQueryPostHandler{T}" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the test.</param>
    public ListQueryPostHandler(GenericClosingRecorder recorder)
    {
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task PostHandleAsync(ListQuery<T> message, List<T>? messageResult, CancellationToken cancellationToken = default)
    {
        _recorder.Record($"post:List<{typeof(T).Name}>:{messageResult?.Count}");
        return Task.CompletedTask;
    }
}

/// <summary>
///     A command with two type parameters, the second shape in #169 and the example in the generic messages guide.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TKey">The key type the command returns.</typeparam>
internal sealed class CreateEntityCommand<TEntity, TKey> : ICommand<TKey>
    where TEntity : IEntity<TKey>
{
    /// <summary>
    ///     Gets the entity to create.
    /// </summary>
    public required TEntity EntityData { get; init; }
}

/// <summary>
///     Returns the key of the entity in the command.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TKey">The key type.</typeparam>
internal sealed class CreateEntityCommandHandler<TEntity, TKey> : ICommandHandler<CreateEntityCommand<TEntity, TKey>, TKey>
    where TEntity : class, IEntity<TKey>
{
    /// <inheritdoc />
    public Task<TKey> HandleAsync(CreateEntityCommand<TEntity, TKey> message, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(message.EntityData.Id);
    }
}

/// <summary>
///     Records the entity type before a <see cref="CreateEntityCommand{TEntity, TKey}" /> is handled.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TKey">The key type.</typeparam>
internal sealed class CreateEntityCommandPreHandler<TEntity, TKey> : ICommandPreHandler<CreateEntityCommand<TEntity, TKey>>
    where TEntity : class, IEntity<TKey>
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly GenericClosingRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="CreateEntityCommandPreHandler{TEntity, TKey}" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the test.</param>
    public CreateEntityCommandPreHandler(GenericClosingRecorder recorder)
    {
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task PreHandleAsync(CreateEntityCommand<TEntity, TKey> message, CancellationToken cancellationToken = default)
    {
        _recorder.Record($"pre:{typeof(TEntity).Name}");
        return Task.CompletedTask;
    }
}

/// <summary>
///     Records the typed result of a <see cref="CreateEntityCommand{TEntity, TKey}" />.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TKey">The key type.</typeparam>
internal sealed class CreateEntityCommandPostHandler<TEntity, TKey> : ICommandPostHandler<CreateEntityCommand<TEntity, TKey>, TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly GenericClosingRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="CreateEntityCommandPostHandler{TEntity, TKey}" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the test.</param>
    public CreateEntityCommandPostHandler(GenericClosingRecorder recorder)
    {
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task PostHandleAsync(CreateEntityCommand<TEntity, TKey> message, TKey? messageResult, CancellationToken cancellationToken = default)
    {
        _recorder.Record($"post:{typeof(TEntity).Name}:{messageResult}");
        return Task.CompletedTask;
    }
}

/// <summary>
///     Records the typed result a <see cref="CreateEntityCommand{TEntity, TKey}" /> completed with.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TKey">The key type.</typeparam>
internal sealed class CreateEntityCommandCompletionHandler<TEntity, TKey>
    : ICommandCompletionHandler<CreateEntityCommand<TEntity, TKey>, TKey>
    where TEntity : class, IEntity<TKey>
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly GenericClosingRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="CreateEntityCommandCompletionHandler{TEntity, TKey}" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the test.</param>
    public CreateEntityCommandCompletionHandler(GenericClosingRecorder recorder)
    {
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task HandleCompletionAsync(
        MessageCompletionContext<CreateEntityCommand<TEntity, TKey>, TKey> context,
        CancellationToken cancellationToken)
    {
        _recorder.Record($"completion:{typeof(TEntity).Name}:{context.MessageResult}");
        return Task.CompletedTask;
    }
}

/// <summary>
///     A command whose handler declares the type parameters in the opposite order.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TKey">The key type.</typeparam>
/// <param name="Entity">The entity to rename.</param>
/// <param name="Name">The new name.</param>
internal sealed record RenameEntityCommand<TEntity, TKey>(TEntity Entity, string Name) : ICommand<string>
    where TEntity : IEntity<TKey>;

/// <summary>
///     Handles <see cref="RenameEntityCommand{TEntity, TKey}" /> with its type parameters declared key first.
/// </summary>
/// <typeparam name="TKey">The key type.</typeparam>
/// <typeparam name="TEntity">The entity type.</typeparam>
internal sealed class RenameEntityCommandHandler<TKey, TEntity> : ICommandHandler<RenameEntityCommand<TEntity, TKey>, string>
    where TEntity : class, IEntity<TKey>
{
    /// <inheritdoc />
    public Task<string> HandleAsync(RenameEntityCommand<TEntity, TKey> message, CancellationToken cancellationToken = default)
    {
        return Task.FromResult($"{typeof(TEntity).Name}:{typeof(TKey).Name}:{message.Name}");
    }
}

/// <summary>
///     A command whose main handler always fails, so its error handler decides the result.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TKey">The key type the command returns.</typeparam>
/// <param name="Entity">The entity the error handler takes the key from.</param>
internal sealed record RejectEntityCommand<TEntity, TKey>(TEntity Entity) : ICommand<TKey>
    where TEntity : IEntity<TKey>;

/// <summary>
///     Fails every <see cref="RejectEntityCommand{TEntity, TKey}" />.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TKey">The key type.</typeparam>
internal sealed class RejectEntityCommandHandler<TEntity, TKey> : ICommandHandler<RejectEntityCommand<TEntity, TKey>, TKey>
    where TEntity : class, IEntity<TKey>
{
    /// <inheritdoc />
    public Task<TKey> HandleAsync(RejectEntityCommand<TEntity, TKey> message, CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException("Rejected.");
    }
}

/// <summary>
///     Recovers a failed <see cref="RejectEntityCommand{TEntity, TKey}" /> with the entity key, declaring its type
///     parameters key first.
/// </summary>
/// <typeparam name="TKey">The key type.</typeparam>
/// <typeparam name="TEntity">The entity type.</typeparam>
internal sealed class RejectEntityCommandErrorHandler<TKey, TEntity> : ICommandErrorHandler<RejectEntityCommand<TEntity, TKey>, TKey>
    where TEntity : class, IEntity<TKey>
{
    /// <inheritdoc />
    public Task HandleErrorAsync(
        MessageErrorContext<RejectEntityCommand<TEntity, TKey>, TKey> context,
        CancellationToken cancellationToken = default)
    {
        context.HandledResult = context.Message.Entity.Id;
        context.Outcome = MessageErrorOutcome.Handled;
        return Task.CompletedTask;
    }
}

/// <summary>
///     A query whose handlers take a type parameter nested inside the message's type argument.
/// </summary>
/// <typeparam name="TPayload">The payload type.</typeparam>
/// <param name="Payload">The payload the handler describes.</param>
internal sealed record EnvelopeQuery<TPayload>(TPayload Payload) : IQuery<string>;

/// <summary>
///     Describes an <see cref="EnvelopeQuery{TPayload}" /> whose payload is a list.
/// </summary>
/// <typeparam name="T">The list item type.</typeparam>
internal sealed class ListEnvelopeQueryHandler<T> : IQueryHandler<EnvelopeQuery<List<T>>, string>
{
    /// <inheritdoc />
    public Task<string> HandleAsync(EnvelopeQuery<List<T>> message, CancellationToken cancellationToken = default)
    {
        return Task.FromResult($"list:{typeof(T).Name}:{message.Payload.Count}");
    }
}

/// <summary>
///     Describes an <see cref="EnvelopeQuery{TPayload}" /> whose payload is an array.
/// </summary>
/// <typeparam name="T">The array element type.</typeparam>
internal sealed class ArrayEnvelopeQueryHandler<T> : IQueryHandler<EnvelopeQuery<T[]>, string>
{
    /// <inheritdoc />
    public Task<string> HandleAsync(EnvelopeQuery<T[]> message, CancellationToken cancellationToken = default)
    {
        return Task.FromResult($"array:{typeof(T).Name}:{message.Payload.Length}");
    }
}

/// <summary>
///     A query with one handler for reference type arguments and one for value type arguments.
/// </summary>
/// <typeparam name="T">The type to describe.</typeparam>
internal sealed record DescribeQuery<T> : IQuery<string>;

/// <summary>
///     Describes a reference type argument.
/// </summary>
/// <typeparam name="T">The type to describe.</typeparam>
internal sealed class ReferenceDescribeQueryHandler<T> : IQueryHandler<DescribeQuery<T>, string>
    where T : class
{
    /// <inheritdoc />
    public Task<string> HandleAsync(DescribeQuery<T> message, CancellationToken cancellationToken = default)
    {
        return Task.FromResult($"reference:{typeof(T).Name}");
    }
}

/// <summary>
///     Describes a value type argument.
/// </summary>
/// <typeparam name="T">The type to describe.</typeparam>
internal sealed class ValueDescribeQueryHandler<T> : IQueryHandler<DescribeQuery<T>, string>
    where T : struct
{
    /// <inheritdoc />
    public Task<string> HandleAsync(DescribeQuery<T> message, CancellationToken cancellationToken = default)
    {
        return Task.FromResult($"value:{typeof(T).Name}");
    }
}

/// <summary>
///     One streamed page.
/// </summary>
/// <typeparam name="T">The item type.</typeparam>
/// <param name="Number">The one-based page number.</param>
/// <param name="Item">The item on the page.</param>
internal sealed record Page<T>(int Number, T Item);

/// <summary>
///     A stream query whose item type nests the message's type parameter.
/// </summary>
/// <typeparam name="T">The item type.</typeparam>
/// <param name="Item">The item every page carries.</param>
/// <param name="Pages">The number of pages to stream.</param>
internal sealed record PagedStreamQuery<T>(T Item, int Pages) : IStreamQuery<Page<T>>;

/// <summary>
///     Streams the requested number of pages.
/// </summary>
/// <typeparam name="T">The item type.</typeparam>
internal sealed class PagedStreamQueryHandler<T> : IStreamQueryHandler<PagedStreamQuery<T>, Page<T>>
{
    /// <inheritdoc />
    public async IAsyncEnumerable<Page<T>> StreamAsync(
        PagedStreamQuery<T> message,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        for (var number = 1; number <= message.Pages; number++)
        {
            await Task.Yield();
            yield return new Page<T>(number, message.Item);
        }
    }
}

/// <summary>
///     A generic event interface that non-generic events implement.
/// </summary>
/// <typeparam name="TEntity">The entity that changed.</typeparam>
internal interface IEntityChanged<TEntity> : IEvent;

/// <summary>
///     A non-generic event that implements <see cref="IEntityChanged{TEntity}" /> once.
/// </summary>
/// <param name="Entity">The customer that changed.</param>
internal sealed record CustomerChangedEvent(Customer Entity) : IEntityChanged<Customer>;

/// <summary>
///     A non-generic event that implements <see cref="IEntityChanged{TEntity}" /> twice, so no single closure of a
///     handler for that interface fits it.
/// </summary>
internal sealed class CustomerAndProductChangedEvent : IEntityChanged<Customer>, IEntityChanged<Product>;

/// <summary>
///     Records the entity type of every <see cref="IEntityChanged{TEntity}" /> event.
/// </summary>
/// <typeparam name="TEntity">The entity that changed.</typeparam>
internal sealed class EntityChangedEventHandler<TEntity> : IEventHandler<IEntityChanged<TEntity>>
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly GenericClosingRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="EntityChangedEventHandler{TEntity}" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the test.</param>
    public EntityChangedEventHandler(GenericClosingRecorder recorder)
    {
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task HandleAsync(IEntityChanged<TEntity> message, CancellationToken cancellationToken = default)
    {
        _recorder.Record($"changed:{typeof(TEntity).Name}");
        return Task.CompletedTask;
    }
}

/// <summary>
///     A plain event.
/// </summary>
internal sealed record NoticeEvent : IEvent;

/// <summary>
///     A generic handler whose type parameter appears nowhere in the message it handles.
/// </summary>
/// <typeparam name="TExtra">A type parameter nothing can bind.</typeparam>
internal sealed class UnboundNoticeEventHandler<TExtra> : IEventHandler<NoticeEvent>
{
    /// <inheritdoc />
    public Task HandleAsync(NoticeEvent message, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
