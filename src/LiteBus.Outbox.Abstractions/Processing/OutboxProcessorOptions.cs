using LiteBus.Messaging.Abstractions.Processing;

namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Defines outbox processor settings.
/// </summary>
/// <remarks>
///     <para>
///         These options are read when the processor instance is created. Register separate processor instances with
///         different options when an application needs different publisher identities or batch sizes.
///     </para>
///     <para>
///         Tuning trade-offs: larger <see cref="ProcessorOptions.BatchSize" /> values reduce store round-trips but increase the number of
///         leases held while dispatchers publish. Longer <see cref="ProcessorOptions.LeaseDuration" /> values tolerate slow brokers but
///         delay
///         recovery when a publisher crashes without releasing leases.
///     </para>
/// </remarks>
public sealed record OutboxProcessorOptions : ProcessorOptions;