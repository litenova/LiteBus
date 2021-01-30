using System.Threading;

namespace Paykan.Messaging.Abstractions
{
    /// <summary>
    /// provides options on how to send the message to its handler
    /// </summary>
    public interface ISendConfiguration
    {
        bool ThrowExceptionOnFindingMultipleHandler { get; set; }

        bool ExecutePostHandleHooks { get; set; }
        CancellationToken CancellationToken { get; set; }
    }

    /// <summary>
    /// provides options on how to publish the message to its handlers
    /// </summary>
    public interface IPublishConfiguration
    {
        CancellationToken CancellationToken { get; set; }
    }
}