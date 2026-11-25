using System;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Creates sample message instances used to verify that durable contracts survive serialization.
/// </summary>
/// <remarks>
///     <para>
///         Host validation round-trips every registered durable message contract through the configured
///         <see cref="IMessageSerializer" /> and compares the result property by property. Each round trip needs
///         an input value, which this port supplies. The default factory builds samples with non-default values
///         for records, classes with a parameterless constructor, and similar shapes.
///     </para>
///     <para>
///         A message type the default factory cannot build, such as one whose constructors all guard their
///         arguments, needs an application factory registered through
///         <c>MessageModuleBuilder.UseMessageSampleFactory</c>. Return <see langword="null" /> for a type the
///         factory cannot sample; the check reports the missing sample so the registration is fixed.
///     </para>
///     <para>
///         This port runs at host validation time, outside any mediation, so it takes no evaluation context.
///     </para>
/// </remarks>
public interface IMessageSampleFactory
{
    /// <summary>
    ///     Creates a sample instance of one message type.
    /// </summary>
    /// <param name="messageType">The message type to sample.</param>
    /// <param name="cancellationToken">A token used to cancel sample creation.</param>
    /// <returns>The sample instance, or <see langword="null" /> when the factory cannot sample the type.</returns>
    ValueTask<object?> CreateSampleAsync(Type messageType, CancellationToken cancellationToken = default);
}
