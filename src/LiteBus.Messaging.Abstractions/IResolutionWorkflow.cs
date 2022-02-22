using System;
using LiteBus.Messaging.Abstractions.Metadata;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Represents the workflow of resolving corresponding handlers of a given <see cref="IMessageDescriptor"/> using <see cref="IServiceProvider"/>
/// </summary>
public interface IResolutionWorkflow
{
    /// <summary>
    ///     resolves the corresponding handlers of the given <see cref="IMessageDescriptor"/> using <see cref="IServiceProvider"/>
    /// </summary>
    /// <param name="serviceProvider">The instance of <see cref="IServiceProvider"/></param>
    /// <param name="messageDescriptor">The given <see cref="IMessageDescriptor"/> instance</param>
    /// <returns></returns>
    IResolutionContext Resolve(IServiceProvider serviceProvider, IMessageDescriptor messageDescriptor);
}