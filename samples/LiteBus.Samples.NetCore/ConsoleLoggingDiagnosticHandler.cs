using LiteBus.Messaging.Abstractions;

namespace LiteBus.Samples.NetCore;

public class ConsoleLoggingDiagnosticHandler : IDiagnosticHandler
{
    public void OnMediationStarting<TMessage>(TMessage message, IExecutionContext context) where TMessage : notnull
    {
        Console.WriteLine($"[LiteBus] Starting mediation of {message.GetType().Name}");
    }

    public void OnMediationCompleted<TMessage, TMessageResult>(TMessage message, TMessageResult result, IExecutionContext context) where TMessage : notnull
    {
        Console.WriteLine($"[LiteBus] Successfully completed {message.GetType().Name}. Result Type: {typeof(TMessageResult).Name}");
    }

    public void OnMediationFaulted<TMessage>(TMessage message, Exception exception, IExecutionContext context) where TMessage : notnull
    {
        var color = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[LiteBus] CRITICAL: {message.GetType().Name} failed!");
        Console.WriteLine($"[LiteBus] Exception: {exception.Message}");
        Console.ForegroundColor = color;
    }
}