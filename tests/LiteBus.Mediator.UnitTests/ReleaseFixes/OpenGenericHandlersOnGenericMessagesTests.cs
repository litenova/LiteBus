using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Queries;
using LiteBus.Queries.Abstractions;
using LiteBus.Testing;
using LiteBus.Testing.Mediation;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.ReleaseFixes.OpenGenericHandlersOnGenericMessages;

/// <summary>
///     Verifies that an open generic pipeline handler over a bare type parameter, such as a guard or a pre-handler that
///     applies to every command, runs for a generic message too.
/// </summary>
/// <remarks>
///     A generic message is registered by its open definition, and an open generic handler is closed only over registered
///     closed messages, so such a handler never ran for <c>CreateEntityCommand&lt;Product&gt;</c>. The closed message is
///     now registered on its first dispatch, which closes every applicable open generic handler over it.
/// </remarks>
[Collection("Sequential")]
public sealed class OpenGenericHandlersOnGenericMessagesTests : LiteBusTestBase
{
    [Fact]
    public async Task An_open_generic_guard_denies_a_generic_command()
    {
        var recorder = new GenericPipelineRecorder();
        var provider = BuildProvider(recorder, commands: builder =>
        {
            builder.Register(typeof(DenyEveryCommandGuard<>));
            builder.Register(typeof(ArchiveCommandHandler<>));
        });

        var act = () => provider.GetRequiredService<ICommandMediator>().SendAsync(new ArchiveCommand<string>("doc"));

        await act.Should().ThrowAsync<LiteBusMessageDeniedException>().ConfigureAwait(false);
        recorder.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task An_open_generic_pre_handler_runs_once_per_send_of_each_closed_generic_command()
    {
        var recorder = new GenericPipelineRecorder();
        var provider = BuildProvider(recorder, commands: builder =>
        {
            builder.Register(typeof(LoggingCommandPreHandler<>));
            builder.Register(typeof(ArchiveCommandHandler<>));
        });
        var mediator = provider.GetRequiredService<ICommandMediator>();

        await mediator.SendAsync(new ArchiveCommand<string>("a")).ConfigureAwait(false);
        await mediator.SendAsync(new ArchiveCommand<string>("b")).ConfigureAwait(false);
        await mediator.SendAsync(new ArchiveCommand<int>(7)).ConfigureAwait(false);

        recorder.Entries.Should().Equal(
            "pre:ArchiveCommand<String>",
            "archived:a",
            "pre:ArchiveCommand<String>",
            "archived:b",
            "pre:ArchiveCommand<Int32>",
            "archived:7");
    }

    [Fact]
    public async Task Idempotency_applies_to_a_generic_command_declared_for_its_closed_type()
    {
        var recorder = new GenericPipelineRecorder();
        var provider = BuildProvider(
            recorder,
            commands: builder =>
            {
                builder.Register(typeof(ArchiveCommandHandler<>));
                builder.Register<ArchiveStringCommandDefinition>();
                builder.EnableIdempotency();
            },
            services => services.AddSingleton<IIdempotencyStore>(new InMemoryIdempotencyStore()));
        var mediator = provider.GetRequiredService<ICommandMediator>();

        await mediator.SendAsync(new ArchiveCommand<string>("same")).ConfigureAwait(false);
        await mediator.SendAsync(new ArchiveCommand<string>("same")).ConfigureAwait(false);

        recorder.Entries.Should().Equal("archived:same");
    }

    [Fact]
    public async Task A_closed_handler_still_overrides_the_open_handler_of_its_generic_definition()
    {
        var recorder = new GenericPipelineRecorder();
        var provider = BuildProvider(recorder, queries: builder =>
        {
            builder.Register(typeof(LookupQueryHandler<>));
            builder.Register<IntLookupQueryHandler>();
        });
        var mediator = provider.GetRequiredService<IQueryMediator>();

        var closed = await mediator.QueryAsync(new LookupQuery<int>()).ConfigureAwait(false);
        var open = await mediator.QueryAsync(new LookupQuery<string>()).ConfigureAwait(false);

        closed.Should().Be("closed:Int32");
        open.Should().Be("open:String");
    }

    /// <summary>
    ///     Builds a provider with the messaging, command, and query modules.
    /// </summary>
    /// <param name="recorder">The recorder the handlers write to.</param>
    /// <param name="commands">Registers the command constructs under test.</param>
    /// <param name="services">Adds application services.</param>
    /// <param name="queries">Registers the query constructs under test.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildProvider(
        GenericPipelineRecorder recorder,
        Action<CommandModuleBuilder>? commands = null,
        Action<IServiceCollection>? services = null,
        Action<QueryModuleBuilder>? queries = null)
    {
        var collection = new ServiceCollection();
        collection.AddSingleton(recorder);
        services?.Invoke(collection);

        return collection
            .AddLiteBus(liteBus =>
            {
                liteBus.AddMessaging(_ => { });
                liteBus.AddCommands(builder => commands?.Invoke(builder));
                liteBus.AddQueries(builder => queries?.Invoke(builder));
            })
            .BuildServiceProvider();
    }
}

/// <summary>
///     Records what the handlers observed, in order.
/// </summary>
internal sealed class GenericPipelineRecorder
{
    /// <summary>
    ///     Holds the recorded entries.
    /// </summary>
    private readonly List<string> _entries = [];

    /// <summary>
    ///     Gets the recorded entries in the order they were written.
    /// </summary>
    public IReadOnlyList<string> Entries => _entries;

    /// <summary>
    ///     Records one entry.
    /// </summary>
    /// <param name="entry">The entry to record.</param>
    public void Record(string entry)
    {
        _entries.Add(entry);
    }

    /// <summary>
    ///     Formats a closed generic type as <c>Name&lt;Argument&gt;</c>.
    /// </summary>
    /// <param name="type">The type to format.</param>
    /// <returns>The readable name.</returns>
    public static string Describe(Type type)
    {
        return type.IsGenericType
            ? $"{type.Name[..type.Name.IndexOf('`', StringComparison.Ordinal)]}<{string.Join(", ", type.GetGenericArguments().Select(argument => argument.Name))}>"
            : type.Name;
    }
}

/// <summary>
///     A generic command handled by its open generic handler.
/// </summary>
/// <typeparam name="TDocument">The archived document type.</typeparam>
/// <param name="Document">The document to archive.</param>
internal sealed record ArchiveCommand<TDocument>(TDocument Document) : ICommand;

/// <summary>
///     Archives every closed <see cref="ArchiveCommand{TDocument}" />.
/// </summary>
/// <typeparam name="TDocument">The archived document type.</typeparam>
internal sealed class ArchiveCommandHandler<TDocument> : ICommandHandler<ArchiveCommand<TDocument>>
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly GenericPipelineRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ArchiveCommandHandler{TDocument}" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the test.</param>
    public ArchiveCommandHandler(GenericPipelineRecorder recorder)
    {
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task HandleAsync(ArchiveCommand<TDocument> message, CancellationToken cancellationToken = default)
    {
        _recorder.Record($"archived:{message.Document}");
        return Task.CompletedTask;
    }
}

/// <summary>
///     Declares an idempotency key for <see cref="ArchiveCommand{TDocument}" /> of <see cref="string" />.
/// </summary>
internal sealed class ArchiveStringCommandDefinition : IIdempotencyDefinition<ArchiveCommand<string>>
{
    /// <inheritdoc />
    public IdempotencyDeclaration Idempotency =>
        IdempotencyDeclaration.KeyedBy<ArchiveCommand<string>>(command => command.Document);
}

/// <summary>
///     Denies every command, through an open generic guard over a bare type parameter.
/// </summary>
/// <typeparam name="TCommand">The command the registry closed this guard over.</typeparam>
internal sealed class DenyEveryCommandGuard<TCommand> : ICommandGuard<TCommand>
    where TCommand : ICommand
{
    /// <inheritdoc />
    public Task<Verdict> DecideAsync(TCommand message, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Verdict.Deny("denied by policy"));
    }
}

/// <summary>
///     Records the command type each closure runs for, through an open generic pre-handler over a bare type parameter.
/// </summary>
/// <typeparam name="TCommand">The command the registry closed this pre-handler over.</typeparam>
internal sealed class LoggingCommandPreHandler<TCommand> : ICommandPreHandler<TCommand>
    where TCommand : ICommand
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly GenericPipelineRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="LoggingCommandPreHandler{TCommand}" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the test.</param>
    public LoggingCommandPreHandler(GenericPipelineRecorder recorder)
    {
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task PreHandleAsync(TCommand message, CancellationToken cancellationToken = default)
    {
        _recorder.Record($"pre:{GenericPipelineRecorder.Describe(typeof(TCommand))}");
        return Task.CompletedTask;
    }
}

/// <summary>
///     A generic query with an open handler and a closed handler for <see cref="int" />.
/// </summary>
/// <typeparam name="TKey">The key type.</typeparam>
internal sealed record LookupQuery<TKey> : IQuery<string>;

/// <summary>
///     Handles every closed <see cref="LookupQuery{TKey}" /> without a closed handler of its own.
/// </summary>
/// <typeparam name="TKey">The key type.</typeparam>
internal sealed class LookupQueryHandler<TKey> : IQueryHandler<LookupQuery<TKey>, string>
{
    /// <inheritdoc />
    public Task<string> HandleAsync(LookupQuery<TKey> message, CancellationToken cancellationToken = default)
    {
        return Task.FromResult($"open:{typeof(TKey).Name}");
    }
}

/// <summary>
///     Handles <see cref="LookupQuery{TKey}" /> of <see cref="int" /> in place of the open handler.
/// </summary>
internal sealed class IntLookupQueryHandler : IQueryHandler<LookupQuery<int>, string>
{
    /// <inheritdoc />
    public Task<string> HandleAsync(LookupQuery<int> message, CancellationToken cancellationToken = default)
    {
        return Task.FromResult("closed:Int32");
    }
}
