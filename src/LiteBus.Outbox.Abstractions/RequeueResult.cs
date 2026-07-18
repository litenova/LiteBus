namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Reports how many dead-lettered outbox messages were requested for replay versus actually requeued.
/// </summary>
/// <param name="Requested">The number of message identifiers supplied to the requeue operation.</param>
/// <param name="Requeued">The number of rows transitioned from dead-lettered back to pending.</param>
public sealed record RequeueResult(int Requested, int Requeued);
