using LiteBus.Testing.Mediation;
using LiteBus.Messaging;
using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging.Abstractions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.Idempotency;

[Collection("Sequential")]
public sealed class IdempotencyConfigurationTests : LiteBusTestBase
{
    [Fact]
    public async Task BlankKey_PropagatesWithoutRunningErrorHandlers()
    {
        var probe = new IdempotencyErrorProbe();
        var provider = BuildProvider(probe);
        var mediator = provider.GetRequiredService<ICommandMediator>();

        var act = async () => await mediator.SendAsync(new BlankKeyCommand { PaymentId = "  " }).ConfigureAwait(false);

        await act.Should().ThrowAsync<IdempotencyConfigurationException>().ConfigureAwait(false);
        probe.Ran.Should().BeFalse();
    }

    [Fact]
    public async Task MissingContextKey_PropagatesWithoutRunningErrorHandlers()
    {
        var probe = new IdempotencyErrorProbe();
        var provider = BuildProvider(probe);
        var mediator = provider.GetRequiredService<ICommandMediator>();

        var act = async () => await mediator.SendAsync(new ContextKeyCommand()).ConfigureAwait(false);

        await act.Should().ThrowAsync<IdempotencyConfigurationException>().ConfigureAwait(false);
        probe.Ran.Should().BeFalse();
    }

    private static ServiceProvider BuildProvider(IdempotencyErrorProbe probe)
    {
        var services = new ServiceCollection();
        services.AddSingleton(probe);
        services.AddSingleton<IIdempotencyStore>(new InMemoryIdempotencyStore());

        return services
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });

                registry.AddCommands(builder =>
                {
                    builder.Register<BlankKeyCommand>();
                    builder.Register<BlankKeyCommandHandler>();
                    builder.Register<BlankKeyCommandDefinition>();
                    builder.Register<BlankKeyCommandErrorHandler>();
                    builder.Register<ContextKeyCommand>();
                    builder.Register<ContextKeyCommandHandler>();
                    builder.Register<ContextKeyCommandDefinition>();
                    builder.Register<ContextKeyCommandErrorHandler>();
                    builder.EnableIdempotency();
                });
            })
            .BuildServiceProvider();
    }
}

public sealed class IdempotencyErrorProbe
{
    public bool Ran { get; set; }
}

internal sealed class BlankKeyCommand : ICommand
{
    public string PaymentId { get; set; } = string.Empty;
}

internal sealed class BlankKeyCommandDefinition : IIdempotencyDefinition<BlankKeyCommand>
{
    public IdempotencyDeclaration Idempotency =>
        IdempotencyDeclaration.KeyedBy<BlankKeyCommand>(command => command.PaymentId);
}

internal sealed class BlankKeyCommandHandler : ICommandHandler<BlankKeyCommand>
{
    public Task HandleAsync(BlankKeyCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        throw new InvalidOperationException("The handler should not run when the key is blank.");
    }
}

internal sealed class BlankKeyCommandErrorHandler : ICommandErrorHandler<BlankKeyCommand>
{
    private readonly IdempotencyErrorProbe _probe;

    public BlankKeyCommandErrorHandler(IdempotencyErrorProbe probe)
    {
        _probe = probe;
    }

    public Task HandleErrorAsync(MessageErrorContext<BlankKeyCommand, object> context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        _probe.Ran = true;
        return Task.CompletedTask;
    }
}

internal sealed class ContextKeyCommand : ICommand;

internal sealed class ContextKeyCommandDefinition : IIdempotencyDefinition<ContextKeyCommand>
{
    public IdempotencyDeclaration Idempotency => IdempotencyDeclaration.KeyedByContext();
}

internal sealed class ContextKeyCommandHandler : ICommandHandler<ContextKeyCommand>
{
    public Task HandleAsync(ContextKeyCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        throw new InvalidOperationException("The handler should not run when no stage supplied a key.");
    }
}

internal sealed class ContextKeyCommandErrorHandler : ICommandErrorHandler<ContextKeyCommand>
{
    private readonly IdempotencyErrorProbe _probe;

    public ContextKeyCommandErrorHandler(IdempotencyErrorProbe probe)
    {
        _probe = probe;
    }

    public Task HandleErrorAsync(MessageErrorContext<ContextKeyCommand, object> context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        _probe.Ran = true;
        return Task.CompletedTask;
    }
}
