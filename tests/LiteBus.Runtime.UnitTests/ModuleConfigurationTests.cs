using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Dependencies;
using LiteBus.Runtime.Modules;

namespace LiteBus.Runtime.UnitTests;

public sealed class ModuleConfigurationTests
{
    [Fact]
    public void GetContext_WhenMissing_ShouldThrowInvalidOperationException()
    {
        var configuration = new ModuleConfiguration(new DependencyRegistry());

        var act = () => configuration.GetContext<FoundationModule>();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void SetContext_ShouldAllowGetContext()
    {
        var configuration = new ModuleConfiguration(new DependencyRegistry());
        var module = new FoundationModule();

        configuration.SetContext(module);

        configuration.GetContext<FoundationModule>().Should().BeSameAs(module);
    }

    [Fact]
    public void SetContext_WithLaterValue_ShouldOverwriteExistingContext()
    {
        var configuration = new ModuleConfiguration(new DependencyRegistry());
        var first = new FoundationModule();
        var second = new FoundationModule();

        configuration.SetContext(first);
        configuration.SetContext(second);

        configuration.GetContext<FoundationModule>().Should().BeSameAs(second);
    }

    [Fact]
    public void TryGetContext_WhenMissing_ShouldReturnFalse()
    {
        var configuration = new ModuleConfiguration(new DependencyRegistry());

        var found = configuration.TryGetContext<FoundationModule>(out var context);

        found.Should().BeFalse();
        context.Should().BeNull();
    }

    [Fact]
    public void GetOrCreateContext_ShouldCreateOnce()
    {
        var configuration = new ModuleConfiguration(new DependencyRegistry());
        var createCount = 0;

        var first = configuration.GetOrCreateContext(() =>
        {
            createCount++;
            return new FoundationModule();
        });
        var second = configuration.GetOrCreateContext(() =>
        {
            createCount++;
            return new FoundationModule();
        });

        first.Should().BeSameAs(second);
        createCount.Should().Be(1);
    }
}
