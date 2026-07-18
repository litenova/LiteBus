using LiteBus.Runtime.Abstractions;

namespace LiteBus.Runtime.UnitTests;

internal sealed class OrderRecordingFoundationModule : IModule

{
    private readonly List<Type> _buildOrder;

    public OrderRecordingFoundationModule(List<Type> buildOrder)

    {
        _buildOrder = buildOrder;
    }

    public void Build(IModuleConfiguration configuration)

    {
        _buildOrder.Add(typeof(FoundationModule));

        configuration.SetContext(new FoundationModule());
    }
}

internal sealed class OrderRecordingDependentModule : IModule, IRequires<OrderRecordingFoundationModule>

{
    private readonly List<Type> _buildOrder;

    public OrderRecordingDependentModule(List<Type> buildOrder)

    {
        _buildOrder = buildOrder;
    }

    public void Build(IModuleConfiguration configuration)

    {
        _buildOrder.Add(typeof(DependentModule));

        _ = configuration.GetContext<FoundationModule>();
    }
}

internal sealed class IndependentModule : IModule

{
    public int BuildCount { get; private set; }

    public void Build(IModuleConfiguration configuration)

    {
        BuildCount++;
    }
}

internal sealed class FoundationModule : IModule

{
    public void Build(IModuleConfiguration configuration)

    {
        configuration.SetContext<FoundationModule>(this);
    }
}

internal sealed class DependentModule : IModule, IRequires<FoundationModule>

{
    public void Build(IModuleConfiguration configuration)

    {
        _ = configuration.GetContext<FoundationModule>();
    }
}

internal sealed class ChainCModule : IModule

{
    public void Build(IModuleConfiguration configuration)

    {
    }
}

internal sealed class ChainBModule : IModule, IRequires<ChainCModule>

{
    public void Build(IModuleConfiguration configuration)

    {
    }
}

internal sealed class ChainAModule : IModule, IRequires<ChainBModule>

{
    public void Build(IModuleConfiguration configuration)

    {
    }
}

internal sealed class CycleAModule : IModule, IRequires<CycleBModule>

{
    public void Build(IModuleConfiguration configuration)

    {
    }
}

internal sealed class CycleBModule : IModule, IRequires<CycleAModule>

{
    public void Build(IModuleConfiguration configuration)

    {
    }
}

internal sealed class MissingDependencyModule : IModule, IRequires<FoundationModule>

{
    public void Build(IModuleConfiguration configuration)

    {
    }
}

internal sealed class RegistrationModule : IModule

{
    private readonly Action<IModuleConfiguration> _configure;

    public RegistrationModule(Action<IModuleConfiguration> configure)

    {
        _configure = configure;
    }

    public void Build(IModuleConfiguration configuration)

    {
        _configure(configuration);
    }
}

internal sealed class BackgroundServiceRegistrationModule : IModule

{
    private readonly Type[] _backgroundServiceTypes;

    public BackgroundServiceRegistrationModule(params Type[] backgroundServiceTypes)

    {
        _backgroundServiceTypes = backgroundServiceTypes;
    }

    public void Build(IModuleConfiguration configuration)

    {
        foreach (var backgroundServiceType in _backgroundServiceTypes)

        {
            configuration.RegisterBackgroundService(backgroundServiceType);
        }
    }
}

internal sealed class TestBackgroundService : IBackgroundService

{
    public Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return Task.CompletedTask;
    }
}

internal sealed class OtherBackgroundService : IBackgroundService

{
    public Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return Task.CompletedTask;
    }
}