using System;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;
using Marten;

namespace LiteBus.Commands.Storage.Marten;

/// <summary>
///     Module for registering the Marten tenant session factory.
/// </summary>
/// <remarks>
///     Declared as a child of <see cref="CommandModule" /> through
///     <see cref="CommandModuleBuilderMartenExtensions.UseMartenTenantSessions(CommandModuleBuilder)" />, so the
///     factory is composed inside <c>AddCommands</c> and cannot be registered without the axis whose handlers open
///     sessions through it. The application registers Marten itself with <c>AddMarten</c>; this module only resolves
///     the application-owned <see cref="IDocumentStore" />.
/// </remarks>
public sealed class MartenTenantSessionModule : IModule, IRequires<CommandModule>
{
    /// <inheritdoc />
    public void Build(IModuleConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(MartenTenantSessionFactory),
            serviceProvider => new MartenTenantSessionFactory(
                (IDocumentStore) (serviceProvider.GetService(typeof(IDocumentStore)) ?? throw new LiteBusDependencyResolutionException(typeof(IDocumentStore)))),
            InstanceLifetime.Singleton));
    }
}
