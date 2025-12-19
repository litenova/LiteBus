using System;
using System.Collections.Generic;

namespace LiteBus.Messaging.Abstractions;

public static class DiagnosticExtensions
{
    extension(IEnumerable<IDiagnosticHandler> diagnosticHandlers)
    {
        public void OnMediationStarting<TMessage>(TMessage message,
                                                  IExecutionContext context) where TMessage : notnull
        {
            foreach (var diagnostic in diagnosticHandlers)
            {
                diagnostic.OnMediationStarting(message, context);
            }
        }

        public void OnMediationCompleted<TMessage, TMessageResult>(TMessage message,
                                                                   TMessageResult result,
                                                                   IExecutionContext context) where TMessage : notnull
        {
            foreach (var diagnostic in diagnosticHandlers)
            {
                diagnostic.OnMediationCompleted(message, result, context);
            }
        }
        
        public void OnMediationFaulted<TMessage>(TMessage message,
                                                 Exception exception,
                                                 IExecutionContext context) where TMessage : notnull
        {
            foreach (var diagnostic in diagnosticHandlers)
            {
                diagnostic.OnMediationFaulted(message, exception, context);
            }
        }
    }
}