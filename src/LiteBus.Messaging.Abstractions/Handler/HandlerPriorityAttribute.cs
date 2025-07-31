using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
/// Specifies the execution priority for an event handler.
/// </summary>
/// <remarks>
/// <para>
/// Handlers are organized into priority groups based on their priority value. All handlers 
/// with the same priority number form a priority group and execute together according to 
/// the concurrency settings defined in <see cref="EventMediationSettings"/>.
/// </para>
/// <para>
/// Lower priority numbers execute before higher priority numbers. For example, all handlers 
/// with priority 1 will complete execution before any handler with priority 2 begins 
/// (when using sequential priority group execution).
/// </para>
/// <para>
/// Handlers without this attribute are assigned a default priority of 0 and will execute 
/// before any explicitly prioritized handlers.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [HandlerPriority(1)]
/// public class ValidationHandler : IEventHandler&lt;OrderCreated&gt;
/// {
///     // This handler executes in priority group 1
/// }
/// 
/// [HandlerPriority(2)]
/// public class ProcessingHandler : IEventHandler&lt;OrderCreated&gt;
/// {
///     // This handler executes in priority group 2, after group 1 completes
/// }
/// 
/// [HandlerPriority(2)]
/// public class NotificationHandler : IEventHandler&lt;OrderCreated&gt;
/// {
///     // This handler also executes in priority group 2, alongside ProcessingHandler
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class)]
public sealed class HandlerPriorityAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="HandlerPriorityAttribute"/> class.
    /// </summary>
    /// <param name="priority">
    /// The execution priority number. Lower numbers execute before higher numbers.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="priority"/> is negative.
    /// </exception>
    public HandlerPriorityAttribute(int priority)
    {
        Priority = priority;
    }

    /// <summary>
    /// Gets the execution priority number.
    /// </summary>
    /// <value>
    /// The priority number where lower values execute before higher values.
    /// </value>
    public int Priority { get; }
}