using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Queries;
using LiteBus.Queries.Abstractions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.ReleaseFixes.TryMediationEscapedRefusal;

/// <summary>
///     Verifies that a refusal the pipeline did not decide still reaches a <c>Try</c> caller as a refusal.
/// </summary>
/// <remarks>
///     A handler can end in a refusal the outer pipeline never decided: a nested command denied by its guard, or a
///     handler that throws <see cref="LiteBusMessageDeniedException" /> or <see cref="LiteBusMessageInvalidException" />
///     itself. <c>TrySendAsync</c> and <c>TryQueryAsync</c> caught the exception and reported
///     <see cref="MediationOutcome.Succeeded" /> with a default value.
/// </remarks>
[Collection("Sequential")]
public sealed class TryMediationEscapedRefusalTests : LiteBusTestBase
{
    [Fact]
    public async Task A_nested_denial_reaches_try_send_of_a_result_command_as_denied()
    {
        var result = await BuildProvider().GetRequiredService<ICommandMediator>()
            .TrySendAsync(new NestingResultCommand())
            .ConfigureAwait(false);

        result.Outcome.Should().Be(MediationOutcome.Denied);
        result.Reason.Should().Be("not allowed");
        result.HasValue.Should().BeFalse();
    }

    [Fact]
    public async Task A_nested_denial_reaches_try_send_of_a_void_command_as_denied()
    {
        var result = await BuildProvider().GetRequiredService<ICommandMediator>()
            .TrySendAsync(new NestingVoidCommand())
            .ConfigureAwait(false);

        result.Outcome.Should().Be(MediationOutcome.Denied);
        result.Reason.Should().Be("not allowed");
    }

    [Fact]
    public async Task A_nested_denial_reaches_try_query_as_denied()
    {
        var result = await BuildProvider().GetRequiredService<IQueryMediator>()
            .TryQueryAsync(new NestingQuery())
            .ConfigureAwait(false);

        result.Outcome.Should().Be(MediationOutcome.Denied);
        result.HasValue.Should().BeFalse();
    }

    [Fact]
    public async Task A_denial_thrown_by_the_handler_keeps_its_reason_and_code()
    {
        var result = await BuildProvider().GetRequiredService<ICommandMediator>()
            .TrySendAsync(new SelfDenyingCommand())
            .ConfigureAwait(false);

        result.Outcome.Should().Be(MediationOutcome.Denied);
        result.Reason.Should().Be("handler denied");
        result.Code.Should().Be("H1");
    }

    [Fact]
    public async Task An_invalid_message_thrown_by_the_handler_keeps_its_failures()
    {
        var result = await BuildProvider().GetRequiredService<ICommandMediator>()
            .TrySendAsync(new SelfRejectingCommand())
            .ConfigureAwait(false);

        result.Outcome.Should().Be(MediationOutcome.Invalid);
        result.Code.Should().Be("V1");
        result.Failures.Should().ContainSingle().Which.Message.Should().Be("amount must be positive");
    }

    /// <summary>
    ///     Builds a provider with the messaging, command, and query modules and every construct in this file.
    /// </summary>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildProvider()
    {
        return new ServiceCollection()
            .AddLiteBus(liteBus =>
            {
                liteBus.AddMessaging(_ => { });
                liteBus.AddCommands(builder =>
                {
                    builder.Register<GuardedCommandHandler>();
                    builder.Register<GuardedCommandGuard>();
                    builder.Register<NestingResultCommandHandler>();
                    builder.Register<NestingVoidCommandHandler>();
                    builder.Register<SelfDenyingCommandHandler>();
                    builder.Register<SelfRejectingCommandHandler>();
                });
                liteBus.AddQueries(builder => builder.Register<NestingQueryHandler>());
            })
            .BuildServiceProvider();
    }
}

/// <summary>
///     A command its guard always denies.
/// </summary>
internal sealed class GuardedCommand : ICommand<int>;

/// <summary>
///     Handles <see cref="GuardedCommand" />, which never happens because the guard denies it.
/// </summary>
internal sealed class GuardedCommandHandler : ICommandHandler<GuardedCommand, int>
{
    /// <inheritdoc />
    public Task<int> HandleAsync(GuardedCommand message, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(1);
    }
}

/// <summary>
///     Denies every <see cref="GuardedCommand" />.
/// </summary>
internal sealed class GuardedCommandGuard : ICommandGuard<GuardedCommand>
{
    /// <inheritdoc />
    public Task<Verdict> DecideAsync(GuardedCommand message, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Verdict.Deny("not allowed"));
    }
}

/// <summary>
///     A result command whose handler sends <see cref="GuardedCommand" />.
/// </summary>
internal sealed class NestingResultCommand : ICommand<int>;

/// <summary>
///     Sends the guarded command and lets its denial escape.
/// </summary>
internal sealed class NestingResultCommandHandler : ICommandHandler<NestingResultCommand, int>
{
    /// <summary>
    ///     The mediator the handler sends the nested command through.
    /// </summary>
    private readonly ICommandMediator _mediator;

    /// <summary>
    ///     Initializes a new instance of the <see cref="NestingResultCommandHandler" /> class.
    /// </summary>
    /// <param name="mediator">The mediator the handler sends the nested command through.</param>
    public NestingResultCommandHandler(ICommandMediator mediator)
    {
        _mediator = mediator;
    }

    /// <inheritdoc />
    public async Task<int> HandleAsync(NestingResultCommand message, CancellationToken cancellationToken = default)
    {
        await _mediator.SendAsync(new GuardedCommand(), cancellationToken: cancellationToken).ConfigureAwait(false);
        return 5;
    }
}

/// <summary>
///     A void command whose handler sends <see cref="GuardedCommand" />.
/// </summary>
internal sealed class NestingVoidCommand : ICommand;

/// <summary>
///     Sends the guarded command and lets its denial escape.
/// </summary>
internal sealed class NestingVoidCommandHandler : ICommandHandler<NestingVoidCommand>
{
    /// <summary>
    ///     The mediator the handler sends the nested command through.
    /// </summary>
    private readonly ICommandMediator _mediator;

    /// <summary>
    ///     Initializes a new instance of the <see cref="NestingVoidCommandHandler" /> class.
    /// </summary>
    /// <param name="mediator">The mediator the handler sends the nested command through.</param>
    public NestingVoidCommandHandler(ICommandMediator mediator)
    {
        _mediator = mediator;
    }

    /// <inheritdoc />
    public async Task HandleAsync(NestingVoidCommand message, CancellationToken cancellationToken = default)
    {
        await _mediator.SendAsync(new GuardedCommand(), cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
///     A query whose handler sends <see cref="GuardedCommand" />.
/// </summary>
internal sealed class NestingQuery : IQuery<int>;

/// <summary>
///     Sends the guarded command and lets its denial escape.
/// </summary>
internal sealed class NestingQueryHandler : IQueryHandler<NestingQuery, int>
{
    /// <summary>
    ///     The mediator the handler sends the nested command through.
    /// </summary>
    private readonly ICommandMediator _mediator;

    /// <summary>
    ///     Initializes a new instance of the <see cref="NestingQueryHandler" /> class.
    /// </summary>
    /// <param name="mediator">The mediator the handler sends the nested command through.</param>
    public NestingQueryHandler(ICommandMediator mediator)
    {
        _mediator = mediator;
    }

    /// <inheritdoc />
    public async Task<int> HandleAsync(NestingQuery message, CancellationToken cancellationToken = default)
    {
        return await _mediator.SendAsync(new GuardedCommand(), cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
///     A command whose handler denies it by throwing.
/// </summary>
internal sealed class SelfDenyingCommand : ICommand<int>;

/// <summary>
///     Throws a denial with a reason and a code.
/// </summary>
internal sealed class SelfDenyingCommandHandler : ICommandHandler<SelfDenyingCommand, int>
{
    /// <inheritdoc />
    public Task<int> HandleAsync(SelfDenyingCommand message, CancellationToken cancellationToken = default)
    {
        throw new LiteBusMessageDeniedException(typeof(SelfDenyingCommand), "handler denied", "H1");
    }
}

/// <summary>
///     A command whose handler rejects it as invalid by throwing.
/// </summary>
internal sealed class SelfRejectingCommand : ICommand;

/// <summary>
///     Throws an invalid-message exception with one failure.
/// </summary>
internal sealed class SelfRejectingCommandHandler : ICommandHandler<SelfRejectingCommand>
{
    /// <inheritdoc />
    public Task HandleAsync(SelfRejectingCommand message, CancellationToken cancellationToken = default)
    {
        throw new LiteBusMessageInvalidException(
            typeof(SelfRejectingCommand),
            [new ValidationFailure("amount must be positive", "Amount", "V1")]);
    }
}
