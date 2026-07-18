using LiteBus.Messaging.Abstractions.Processing;

namespace LiteBus.Inbox.Abstractions;

/// <summary>
///     Defines inbox processor settings.
/// </summary>
/// <remarks>
///     <para>
///         These options are read when the processor instance is created. Register separate processor instances with
///         different options when an application needs different worker identities or batch sizes.
///     </para>
///     <para>
///         Tuning trade-offs: larger <see cref="ProcessorOptions.BatchSize" /> values reduce store round-trips but increase the number of
///         leases held during dispatch and the blast radius when a worker stops mid-pass. Longer
///         <see cref="ProcessorOptions.LeaseDuration" />
///         values tolerate slow handlers but delay recovery when a worker crashes without releasing leases.
///     </para>
/// </remarks>
public sealed record InboxProcessorOptions : ProcessorOptions;