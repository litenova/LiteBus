using System;

namespace LiteBus.Messaging.Extensions.MicrosoftDependencyInjection;

/// <summary>
///     Represents an internal module responsible for configuring and building message-related components.
/// </summary>
internal sealed class MessageModule : IModule
{
    private readonly Action<MessageModuleBuilder> _builder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="MessageModule" /> class with the provided builder action.
    /// </summary>
    /// <param name="builder">The builder action used to configure the message module.</param>
    public MessageModule(Action<MessageModuleBuilder> builder)
    {
        _builder = builder;
    }

    /// <summary>
    ///     Builds the message module using the provided configuration.
    /// </summary>
    /// <param name="configuration">The configuration for the message module.</param>
    public void Build(IModuleConfiguration configuration)
    {
        _builder(new MessageModuleBuilder(configuration.MessageRegistry));
    }
}