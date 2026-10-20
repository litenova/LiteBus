using LiteBus.Events.Abstractions;

namespace Systematic.Domain.Events;

/// <summary>
///     An event in a namespace that starts with "System" but is not the BCL, used to check the registry's namespace filter.
/// </summary>
public sealed record SystematicEvent : IEvent;
