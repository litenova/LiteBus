using System;

namespace LiteBus.Messaging.Abstractions;

public interface IDiagnosticHandler
{
    // Called at the very beginning of the Mediate call
    void OnMediationStarting<TMessage>(TMessage message, IExecutionContext context) where TMessage : notnull;

    // Called after all handlers (pre, main, post) have finished successfully
    void OnMediationCompleted<TMessage, TMessageResult>(TMessage message, TMessageResult result, IExecutionContext context) where TMessage : notnull;

    // Called if an exception is thrown anywhere in the pipeline
    void OnMediationFaulted<TMessage>(TMessage message, Exception exception, IExecutionContext context) where TMessage : notnull;
}