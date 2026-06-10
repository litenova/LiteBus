using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Runtime.Dependencies;
using LiteBus.Runtime.Modules;

namespace LiteBus.Runtime.UnitTests;

public sealed class DependencyRegistryTests
{
    [Fact]
    public void Register_WithDuplicateTypeDescriptor_ShouldIgnoreDuplicate()
    {
        var registry = new DependencyRegistry();
        var descriptor = new DependencyDescriptor(typeof(ITestService), typeof(TestServiceA));

        registry.Register(descriptor);
        registry.Register(new DependencyDescriptor(typeof(ITestService), typeof(TestServiceA)));

        registry.Count.Should().Be(1);
    }

    [Fact]
    public void Register_WithConflictingBindingForSameServiceType_ShouldThrowLiteBusConfigurationException()
    {
        var registry = new DependencyRegistry();

        registry.Register(new DependencyDescriptor(typeof(ITestService), typeof(TestServiceA)));

        var act = () => registry.Register(new DependencyDescriptor(typeof(ITestService), typeof(TestServiceB)));

        act.Should().Throw<LiteBusConfigurationException>()
            .WithMessage("*ITestService*");
    }

    [Fact]
    public void Register_ShouldEnumerateDescriptors()
    {
        var registry = new DependencyRegistry();
        var descriptor = new DependencyDescriptor(typeof(ITestService), typeof(TestServiceA));

        registry.Register(descriptor);

        registry.Should().ContainSingle().Which.Should().Be(descriptor);
    }

    [Fact]
    public void RegisterCollection_ShouldAllowMultipleImplementations()
    {
        var registry = new DependencyRegistry();

        registry.RegisterCollection(DependencyDescriptor.ForCollection(typeof(ITestService), typeof(TestServiceA)));
        registry.RegisterCollection(DependencyDescriptor.ForCollection(typeof(ITestService), typeof(TestServiceB)));

        registry.Count.Should().Be(2);
        registry.Count(descriptor => descriptor.DependencyType == typeof(ITestService)).Should().Be(2);
    }

    [Fact]
    public void Register_WithNullDescriptor_ShouldThrowArgumentNullException()
    {
        var registry = new DependencyRegistry();

        var act = () => registry.Register(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ModuleConfiguration_RegisterBackgroundService_ShouldExposeRegisteredType()
    {
        var configuration = new ModuleConfiguration(new DependencyRegistry());

        configuration.RegisterBackgroundService(typeof(TestBackgroundService));

        configuration.BackgroundServices.Should().ContainSingle(type => type == typeof(TestBackgroundService));
    }
}
