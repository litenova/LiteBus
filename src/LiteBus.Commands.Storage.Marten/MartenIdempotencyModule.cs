using System;
using LiteBus.Messaging.Abstractions;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;
using Marten;

namespace LiteBus.Commands.Storage.Marten;

/// <summary>
///     Module for registering the Marten idempotency store.
/// </summary>
/// <remarks>
///     Declared as a child of <see cref="CommandModule" /> through
///     <see cref="CommandModuleBuilderMartenExtensions.UseMartenIdempotencyStore(CommandModuleBuilder, Action{MartenIdempotencyStoreOptions})" />, so the store is composed inside
///     <c>AddCommands</c> and cannot be registered without the axis whose shortcuts claim through it. The application
///     registers Marten itself with <c>AddMarten</c>; this module only resolves the application-owned
///     <see cref="IDocumentStore" /> and the messaging <see cref="TimeProvider" />.
/// </remarks>
public sealed class MartenIdempotencyModule : IModule, IRequires<CommandModule>
{
    /// <summary>
    ///     Gets the claim timeout configuration captured at registration time.
    /// </summary>
    private readonly MartenIdempotencyStoreOptions _options;

    /// <summary>
    ///     Initializes a new instance of the <see cref="MartenIdempotencyModule" /> class.
    /// </summary>
    /// <param name="options">The claim timeout configuration.</param>
    public MartenIdempotencyModule(MartenIdempotencyStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <inheritdoc />
    public void Build(IModuleConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var options = _options;
        options.Validate();

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IIdempotencyStore),
            serviceProvider => new MartenIdempotencyStore(
                (IDocumentStore) (serviceProvider.GetService(typeof(IDocumentStore)) ?? throw new LiteBusDependencyResolutionException(typeof(IDocumentStore))),
                options,
                (TimeProvider?) serviceProvider.GetService(typeof(TimeProvider))),
            InstanceLifetime.Singleton));
    }
}
