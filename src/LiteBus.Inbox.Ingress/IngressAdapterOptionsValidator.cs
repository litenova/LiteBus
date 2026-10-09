using System;

namespace LiteBus.Inbox.Ingress;

/// <summary>
///     Validates broker-specific ingress consumer bounds shared by the inbox ingress adapter modules.
/// </summary>
/// <remarks>
///     Azure Service Bus and AWS SQS expose native consumer knobs that the provider-neutral
///     <see cref="TransportInboxIngressSafetyOptions.Validate" /> does not cover. Adapter modules call this helper
///     so invalid values fail at composition time instead of inside the broker SDK.
/// </remarks>
internal static class IngressAdapterOptionsValidator
{
    /// <summary>
    ///     Validates the native prefetch count applied by brokers that support unacknowledged-delivery prefetch.
    /// </summary>
    /// <param name="prefetchCount">The configured prefetch count.</param>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public static void ValidatePrefetchCount(int prefetchCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(prefetchCount);
    }

    /// <summary>
    ///     Validates the native callback concurrency applied by brokers that expose a separate handler limit.
    /// </summary>
    /// <param name="maxConcurrentCalls">The configured callback concurrency, or <see langword="null" /> when unset.</param>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than one.</exception>
    public static void ValidateMaxConcurrentCalls(int? maxConcurrentCalls)
    {
        if (maxConcurrentCalls is { } concurrentCalls)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(concurrentCalls, 1);
        }
    }

    /// <summary>
    ///     Validates the message count requested per receive call by transports that receive a broker batch.
    /// </summary>
    /// <param name="receiveBatchSize">The configured receive batch size.</param>
    /// <exception cref="ArgumentOutOfRangeException">The value is outside one through ten.</exception>
    public static void ValidateReceiveBatchSize(int receiveBatchSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(receiveBatchSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(receiveBatchSize, 10);
    }
}
