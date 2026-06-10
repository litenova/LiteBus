using System;
using System.Reflection;
using LiteBus.Inbox.Abstractions.Exceptions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Inbox.Ingress;

/// <summary>
///     Shared acknowledgement policy for transport inbox ingress consumers.
/// </summary>
public static class IngressAckPolicy
{
    /// <summary>
    ///     Determines whether a failed delivery should be requeued for retry.
    /// </summary>
    /// <param name="exception">The exception thrown while accepting the delivery.</param>
    /// <param name="requeueOnFailure">A value indicating whether failed store writes should be requeued by the broker.</param>
    /// <returns><see langword="true" /> when the broker should requeue the message; otherwise <see langword="false" />.</returns>
    public static bool ShouldRequeue(Exception exception, bool requeueOnFailure)
    {
        if (!requeueOnFailure)
        {
            return false;
        }

        exception = UnwrapException(exception);

        return exception is not (
            MessageContractNotRegisteredException
            or InboxDispatchException
            or InboxStorageException
            or InvalidOperationException
            or ArgumentException
            or FormatException
            or System.Text.Json.JsonException);
    }

    /// <summary>
    ///     Unwraps reflection and aggregate wrappers so acknowledgement policy inspects the root failure.
    /// </summary>
    /// <param name="exception">The exception observed by the consumer.</param>
    /// <returns>The root exception thrown by inbox acceptance.</returns>
    public static Exception UnwrapException(Exception exception)
    {
        while (true)
        {
            switch (exception)
            {
                case TargetInvocationException target when target.InnerException is not null:
                    exception = target.InnerException;
                    continue;
                case AggregateException aggregate when aggregate.InnerExceptions.Count == 1:
                    exception = aggregate.InnerExceptions[0];
                    continue;
                default:
                    return exception;
            }
        }
    }
}
