using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.DeclarationGuards;

/// <summary>
///     Verifies that a guard derived from <see cref="AuthorizationGuard{TMessage}" /> allows exemptions, enforces the
///     declared policy through the authorization check, and denies what is undeclared.
/// </summary>
[Collection("Sequential")]
public sealed class AuthorizationGuardTests : LiteBusTestBase
{
    /// <summary>
    ///     Verifies that an exempt message is allowed without calling the authorization check.
    /// </summary>
    [Fact]
    public async Task An_exempt_message_is_allowed_without_authorizing()
    {
        var recorder = new AuthorizationRecorder();
        var provider = BuildProvider(
            recorder,
            new FakeAccessAuthorizer(),
            typeof(BrowseGuardedVaultCommand),
            typeof(BrowseGuardedVaultCommandHandler),
            typeof(StubAuthorizationGuard<BrowseGuardedVaultCommand>));
        var command = new BrowseGuardedVaultCommand();

        await provider.GetRequiredService<ICommandMediator>().SendAsync(command).ConfigureAwait(false);

        command.HandlerRan.Should().BeTrue();
        recorder.Policies.Should().BeEmpty();
    }

    /// <summary>
    ///     Verifies that the declared policy reaches the authorization check.
    /// </summary>
    [Fact]
    public async Task The_declared_policy_reaches_AuthorizeAsync()
    {
        var recorder = new AuthorizationRecorder();
        var provider = BuildProvider(
            recorder,
            new FakeAccessAuthorizer("vaults.open"),
            typeof(OpenGuardedVaultCommand),
            typeof(OpenGuardedVaultCommandHandler),
            typeof(StubAuthorizationGuard<OpenGuardedVaultCommand>));
        var command = new OpenGuardedVaultCommand();

        await provider.GetRequiredService<ICommandMediator>().SendAsync(command).ConfigureAwait(false);

        command.HandlerRan.Should().BeTrue();
        recorder.Policies.Should().Equal("vaults.open");
    }

    /// <summary>
    ///     Verifies that a denial from the authorization check refuses the message with its reason and code.
    /// </summary>
    [Fact]
    public async Task A_denial_from_AuthorizeAsync_refuses_the_message()
    {
        var provider = BuildProvider(
            new AuthorizationRecorder(),
            new FakeAccessAuthorizer(),
            typeof(OpenGuardedVaultCommand),
            typeof(OpenGuardedVaultCommandHandler),
            typeof(StubAuthorizationGuard<OpenGuardedVaultCommand>));
        var command = new OpenGuardedVaultCommand();

        var act = async () => await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(command).ConfigureAwait(false);

        var thrown = await act.Should().ThrowAsync<LiteBusMessageDeniedException>().ConfigureAwait(false);
        thrown.Which.Reason.Should().Be("the caller is not authorized under vaults.open");
        thrown.Which.Code.Should().Be("test.forbidden");
        command.HandlerRan.Should().BeFalse();
    }

    /// <summary>
    ///     Verifies that an undeclared message is denied with the stable code the base guard defines.
    /// </summary>
    [Fact]
    public async Task An_undeclared_message_is_denied_with_the_stable_code()
    {
        var provider = BuildProvider(
            new AuthorizationRecorder(),
            new FakeAccessAuthorizer(),
            typeof(DraftGuardedVaultCommand),
            typeof(DraftGuardedVaultCommandHandler),
            typeof(StubAuthorizationGuard<DraftGuardedVaultCommand>));
        var command = new DraftGuardedVaultCommand();

        var act = async () => await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(command).ConfigureAwait(false);

        var thrown = await act.Should().ThrowAsync<LiteBusMessageDeniedException>().ConfigureAwait(false);
        thrown.Which.Code.Should().Be(AuthorizationGuard<DraftGuardedVaultCommand>.UndeclaredCode);
        AuthorizationGuard<DraftGuardedVaultCommand>.UndeclaredCode.Should().Be("declaration.missing");
        thrown.Which.Reason.Should().Contain("DraftGuardedVaultCommand").And.Contain("AuthorizationDeclaration");
        command.HandlerRan.Should().BeFalse();
    }

    /// <summary>
    ///     Verifies that one open generic guard enforces authorized commands and allows exempt ones.
    /// </summary>
    [Fact]
    public async Task One_open_generic_guard_covers_authorized_and_exempt_commands()
    {
        var recorder = new AuthorizationRecorder();
        var provider = BuildProvider(
            recorder,
            new FakeAccessAuthorizer("vaults.open"),
            typeof(OpenGuardedVaultCommand),
            typeof(OpenGuardedVaultCommandHandler),
            typeof(SealGuardedVaultCommand),
            typeof(SealGuardedVaultCommandHandler),
            typeof(BrowseGuardedVaultCommand),
            typeof(BrowseGuardedVaultCommandHandler),
            typeof(StubAuthorizationGuard<>));
        var allowed = new OpenGuardedVaultCommand();
        var exempt = new BrowseGuardedVaultCommand();
        var denied = new SealGuardedVaultCommand();

        await provider.GetRequiredService<ICommandMediator>().SendAsync(allowed).ConfigureAwait(false);
        await provider.GetRequiredService<ICommandMediator>().SendAsync(exempt).ConfigureAwait(false);

        var act = async () => await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(denied).ConfigureAwait(false);

        var thrown = await act.Should().ThrowAsync<LiteBusMessageDeniedException>().ConfigureAwait(false);
        thrown.Which.Reason.Should().Contain("vaults.seal");

        allowed.HandlerRan.Should().BeTrue();
        exempt.HandlerRan.Should().BeTrue();
        denied.HandlerRan.Should().BeFalse();
        recorder.Policies.Should().Equal("vaults.open", "vaults.seal");
    }

    /// <summary>
    ///     Builds a provider registering the given types with the authorization recorder and authorizer.
    /// </summary>
    /// <param name="recorder">The recorder shared with the guards under test.</param>
    /// <param name="authorizer">The authorizer shared with the guards under test.</param>
    /// <param name="types">The command, handler, and guard types to register.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildProvider(AuthorizationRecorder recorder, FakeAccessAuthorizer authorizer, params Type[] types)
    {
        var services = new ServiceCollection();
        services.AddSingleton(recorder);
        services.AddSingleton(authorizer);

        return services
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });

                registry.AddCommands(builder =>
                {
                    foreach (var type in types)
                    {
                        builder.Register(type);
                    }
                });
            })
            .BuildServiceProvider();
    }
}
