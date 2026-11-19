using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Registry;
using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Testing;

namespace LiteBus.Mediator.UnitTests.Registry.RegistrationConflict;

/// <summary>
///     Verifies that a framework default yields to an explicit declaration through <c>TryAddDeclaration</c>.
/// </summary>
public sealed class TryAddDeclarationTests : LiteBusTestBase
{
    /// <summary>
    ///     Verifies that adding when absent records the value and returns true.
    /// </summary>
    [Fact]
    public void Adding_when_absent_records_the_value_and_returns_true()
    {
        var registry = new MessageRegistry();
        registry.Register(typeof(FallbackScopeCommand));

        var added = registry.TryAddDeclaration(MessageDeclarationItem.For<IFallbackScope, FallbackScopeValue>(
            new FallbackScopeValue("framework.default")));

        added.Should().BeTrue();
        registry.Find(typeof(FallbackScopeCommand))!.Metadata.TryGet<FallbackScopeValue>(out var stored)
            .Should().BeTrue();
        stored!.Name.Should().Be("framework.default");
    }

    /// <summary>
    ///     Verifies that adding an identical value returns false and keeps the recorded value.
    /// </summary>
    [Fact]
    public void Adding_an_identical_value_returns_false_and_keeps_the_recorded_value()
    {
        var registry = new MessageRegistry();
        registry.Register(typeof(FallbackScopeCommand));
        registry.TryAddDeclaration(MessageDeclarationItem.For<IFallbackScope, FallbackScopeValue>(
            new FallbackScopeValue("framework.default"))).Should().BeTrue();

        var added = registry.TryAddDeclaration(MessageDeclarationItem.For<IFallbackScope, FallbackScopeValue>(
            new FallbackScopeValue("framework.default")));

        added.Should().BeFalse();
        registry.Find(typeof(FallbackScopeCommand))!.Metadata.TryGet<FallbackScopeValue>(out var stored)
            .Should().BeTrue();
        stored!.Name.Should().Be("framework.default");
    }

    /// <summary>
    ///     Verifies that adding a conflicting value returns false without throwing and keeps the recorded value.
    /// </summary>
    [Fact]
    public void Adding_a_conflicting_value_returns_false_without_throwing_and_keeps_the_recorded_value()
    {
        var registry = new MessageRegistry();
        registry.Register(typeof(FallbackScopeCommand));
        registry.AddDeclaration(MessageDeclarationItem.For<IFallbackScope, FallbackScopeValue>(
            new FallbackScopeValue("explicit.choice")));

        var added = registry.TryAddDeclaration(MessageDeclarationItem.For<IFallbackScope, FallbackScopeValue>(
            new FallbackScopeValue("framework.default")));

        added.Should().BeFalse();
        registry.Find(typeof(FallbackScopeCommand))!.Metadata.TryGet<FallbackScopeValue>(out var stored)
            .Should().BeTrue();
        stored!.Name.Should().Be("explicit.choice");
    }

    /// <summary>
    ///     Verifies that a null declaration is rejected.
    /// </summary>
    [Fact]
    public void A_null_declaration_is_rejected()
    {
        var registry = new MessageRegistry();

        var act = () => registry.TryAddDeclaration(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    /// <summary>
    ///     Verifies that a value that is not an instance of its key type is rejected.
    /// </summary>
    [Fact]
    public void A_value_that_is_not_an_instance_of_its_key_type_is_rejected()
    {
        var registry = new MessageRegistry();

        var act = () => registry.TryAddDeclaration(new MessageDeclarationItem
        {
            MessageType = typeof(IFallbackScope),
            DeclarationType = typeof(FallbackScopeValue),
            Value = "not a fallback scope value"
        });

        act.Should().Throw<MessageDeclarationException>()
            .WithMessage("*not assignable*");
    }

    /// <summary>
    ///     The marker a framework default is scoped to in these tests.
    /// </summary>
    private interface IFallbackScope
    {
    }

    /// <summary>
    ///     The declaration value compared by the framework default in these tests.
    /// </summary>
    /// <param name="Name">The declared name.</param>
    private sealed record FallbackScopeValue(string Name);

    /// <summary>
    ///     A message in the fallback scope.
    /// </summary>
    private sealed class FallbackScopeCommand : IFallbackScope
    {
    }
}
