using System.Reflection;
using LiteBus.Messaging;
using LiteBus.Runtime;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Composition;
using LiteBus.Runtime.Modules;

namespace LiteBus.Runtime.UnitTests.ApplicationModules;

public sealed class ApplicationModuleExtensionsTests
{
    [Fact]
    public void AddApplicationModule_Generic_ShouldConfigureBuilder()
    {
        RecordingGenericModule.Configured = false;
        var builder = new LiteBusBuilder(new ModuleRegistry());

        var result = builder.AddApplicationModule<RecordingGenericModule>();

        result.Should().BeSameAs(builder);
        RecordingGenericModule.Configured.Should().BeTrue();
        builder.Modules.Find<MessageModule>().Should().NotBeNull();
    }

    [Fact]
    public void AddApplicationModule_Instance_ShouldConfigureBuilder()
    {
        var module = new RecordingInstanceModule();
        var builder = new LiteBusBuilder(new ModuleRegistry());

        var result = builder.AddApplicationModule(module);

        result.Should().BeSameAs(builder);
        module.Configured.Should().BeTrue();
        builder.Modules.Find<MessageModule>().Should().NotBeNull();
    }

    [Fact]
    public void AddApplicationModulesFromAssembly_ShouldConfigurePublicAndInternalInTypeNameOrder()
    {
        ApplicationModuleOrderRecorder.Entries.Clear();
        var builder = new LiteBusBuilder(new ModuleRegistry());
        var assembly = new FakeApplicationModuleAssembly(
            typeof(ZebraTestModule),
            typeof(string),
            typeof(AbstractTestModule),
            typeof(MiddleTestModule),
            typeof(PrivateTestModule),
            typeof(NoCtorTestModule),
            typeof(AppleTestModule));

        builder.AddApplicationModulesFromAssembly(assembly);

        ApplicationModuleOrderRecorder.Entries.Should().Equal("Apple", "Middle", "Zebra");
    }

    [Fact]
    public void AddApplicationModule_Generic_WithNullBuilder_ShouldThrow()
    {
        ILiteBusBuilder builder = null!;

        var act = () => builder.AddApplicationModule<RecordingGenericModule>();

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddApplicationModule_Instance_WithNullModule_ShouldThrow()
    {
        var builder = new LiteBusBuilder(new ModuleRegistry());

        var act = () => builder.AddApplicationModule(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddApplicationModulesFromAssembly_WithNullAssembly_ShouldThrow()
    {
        var builder = new LiteBusBuilder(new ModuleRegistry());

        var act = () => builder.AddApplicationModulesFromAssembly(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    private sealed class PrivateTestModule : IApplicationModule
    {
        public void Configure(ILiteBusBuilder liteBus)
        {
            ApplicationModuleOrderRecorder.Entries.Add("Private");
        }
    }

    private sealed class FakeApplicationModuleAssembly : Assembly
    {
        private readonly Type[] _types;

        public FakeApplicationModuleAssembly(params Type[] types)
        {
            _types = types;
        }

        public override Type[] GetTypes()
        {
            return _types;
        }
    }
}

internal sealed class RecordingGenericModule : IApplicationModule
{
    public static bool Configured;

    public void Configure(ILiteBusBuilder liteBus)
    {
        Configured = true;
        liteBus.AddMessaging(_ =>
        {
        });
    }
}

internal sealed class RecordingInstanceModule : IApplicationModule
{
    public bool Configured;

    public void Configure(ILiteBusBuilder liteBus)
    {
        Configured = true;
        liteBus.AddMessaging(_ =>
        {
        });
    }
}

internal static class ApplicationModuleOrderRecorder
{
    public static List<string> Entries { get; } = [];
}

internal sealed class AppleTestModule : IApplicationModule
{
    public void Configure(ILiteBusBuilder liteBus)
    {
        ApplicationModuleOrderRecorder.Entries.Add("Apple");
    }
}

internal sealed class MiddleTestModule : IApplicationModule
{
    public void Configure(ILiteBusBuilder liteBus)
    {
        ApplicationModuleOrderRecorder.Entries.Add("Middle");
    }
}

/// <summary>
///     A public application module proving discovery includes public types.
/// </summary>
public sealed class ZebraTestModule : IApplicationModule
{
    /// <inheritdoc />
    public void Configure(ILiteBusBuilder liteBus)
    {
        ApplicationModuleOrderRecorder.Entries.Add("Zebra");
    }
}

internal abstract class AbstractTestModule : IApplicationModule
{
    public abstract void Configure(ILiteBusBuilder liteBus);
}

internal sealed class NoCtorTestModule : IApplicationModule
{
    public NoCtorTestModule(string _)
    {
    }

    public void Configure(ILiteBusBuilder liteBus)
    {
        ApplicationModuleOrderRecorder.Entries.Add("NoCtor");
    }
}
