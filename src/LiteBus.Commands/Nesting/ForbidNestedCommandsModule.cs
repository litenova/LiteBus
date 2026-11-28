using System;
using LiteBus.Messaging.Abstractions;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Commands.Nesting;

/// <summary>
///     Registers the rule that forbids a command sent from inside another command's handling.
/// </summary>
/// <remarks>
///     Declared as a child of <see cref="CommandModule" /> through
///     <see cref="CommandModuleBuilderNestingExtensions.ForbidNestedCommands(CommandModuleBuilder)" />, so the rule is
///     composed inside <c>AddCommands</c> and cannot be registered without the axis it guards.
/// </remarks>
internal sealed class ForbidNestedCommandsModule : IModule
{
    /// <inheritdoc />
    public void Build(IModuleConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        configuration.DependencyRegistry.RegisterCollection(DependencyDescriptor.ForCollection(
            typeof(IMediationNestingRule),
            typeof(ForbidNestedCommandsRule),
            InstanceLifetime.Singleton));
    }
}
