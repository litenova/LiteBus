using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Registry;
using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.Registry.RegistrationConflict;

/// <summary>
///     Verifies that an identical declaration default is a no-op while a conflicting one fails composition naming both sources.
/// </summary>
[Collection("Sequential")]
public sealed class DeclarationRegistrationConflictTests : LiteBusTestBase
{
    /// <summary>
    ///     Verifies that repeating a default with the same value composes and keeps the value.
    /// </summary>
    [Fact]
    public void An_identical_default_repeat_composes_and_keeps_the_value()
    {
        var accessor = Build(messaging => messaging
                .DeclareDefault<IConflictScope, ConflictScopeValue>(new ConflictScopeValue("family.default"))
                .DeclareDefault<IConflictScope, ConflictScopeValue>(new ConflictScopeValue("family.default")))
            .GetRequiredService<IMessageMetadataAccessor>();

        accessor.TryGet<ConflictScopeCommand, ConflictScopeValue>(out var inherited).Should().BeTrue();
        inherited!.Name.Should().Be("family.default");
    }

    /// <summary>
    ///     Verifies that repeating a default with a different value fails naming both sources.
    /// </summary>
    [Fact]
    public void A_conflicting_default_repeat_fails_naming_both_sources()
    {
        var act = () => Build(messaging => messaging
            .DeclareDefault<IConflictScope, ConflictScopeValue>(new ConflictScopeValue("first"))
            .DeclareDefault<IConflictScope, ConflictScopeValue>(new ConflictScopeValue("second")));

        act.Should().Throw<PipelineContractException>()
            .WithMessage("*a composition default*a composition default*ConflictScopeValue*IConflictScope*");
    }

    /// <summary>
    ///     Verifies that adding the same declaration twice to the registry keeps one value.
    /// </summary>
    [Fact]
    public void An_identical_registry_repeat_keeps_one_value()
    {
        var registry = new MessageRegistry();
        registry.Register(typeof(ConflictScopeCommand));
        registry.AddDeclaration(MessageDeclarationItem.For<IConflictScope, ConflictScopeValue>(
            new ConflictScopeValue("family.default")));

        var act = () => registry.AddDeclaration(MessageDeclarationItem.For<IConflictScope, ConflictScopeValue>(
            new ConflictScopeValue("family.default")));

        act.Should().NotThrow();
        registry.Find(typeof(ConflictScopeCommand))!.Metadata.TryGet<ConflictScopeValue>(out var stored)
            .Should().BeTrue();
        stored!.Name.Should().Be("family.default");
    }

    /// <summary>
    ///     Verifies that adding a conflicting declaration to the registry fails naming both sources.
    /// </summary>
    [Fact]
    public void A_conflicting_registry_repeat_fails_naming_both_sources()
    {
        var registry = new MessageRegistry();
        registry.Register(typeof(ConflictScopeCommand));
        registry.AddDeclaration(MessageDeclarationItem.For<IConflictScope, ConflictScopeValue>(
            new ConflictScopeValue("first")));

        var act = () => registry.AddDeclaration(MessageDeclarationItem.For<IConflictScope, ConflictScopeValue>(
            new ConflictScopeValue("second")));

        act.Should().Throw<PipelineContractException>()
            .WithMessage("*a composition default*a composition default*ConflictScopeValue*IConflictScope*");
    }

    /// <summary>
    ///     Builds a provider over the conflict scope command with the given messaging configuration.
    /// </summary>
    /// <param name="messaging">The messaging configuration under test.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider Build(Action<MessageModuleBuilder> messaging)
    {
        return new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(messaging);

                registry.AddCommands(builder =>
                {
                    builder.Register<ConflictScopeCommand>();
                    builder.Register<ConflictScopeCommandHandler>();
                });
            })
            .BuildServiceProvider();
    }

    /// <summary>
    ///     The marker a declaration default is scoped to in these tests.
    /// </summary>
    private interface IConflictScope
    {
    }

    /// <summary>
    ///     The declaration value compared by the conflict rule in these tests.
    /// </summary>
    /// <param name="Name">The declared name.</param>
    private sealed record ConflictScopeValue(string Name);

    /// <summary>
    ///     A command in the conflict scope.
    /// </summary>
    private sealed class ConflictScopeCommand : ICommand, IConflictScope
    {
    }

    /// <summary>
    ///     Handles <see cref="ConflictScopeCommand" />.
    /// </summary>
    private sealed class ConflictScopeCommandHandler : ICommandHandler<ConflictScopeCommand>
    {
        /// <inheritdoc />
        public Task HandleAsync(ConflictScopeCommand message, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
