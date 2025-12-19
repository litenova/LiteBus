using System.Diagnostics;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Samples.NetCore;

public class OtelDiagnosticHandler : IDiagnosticHandler
{
    private static readonly ActivitySource Source = new("LiteBus.Messaging");
    private const string ActivityKey = "LiteBus.Mediation.Activity";

    public void OnMediationStarting<TMessage>(TMessage message, IExecutionContext context) where TMessage : notnull
    {
        // Start a new activity for the mediation process
        var activity = Source.StartActivity($"Mediation_{message.GetType().Name}");
        
        if (activity != null)
        {
            activity.SetTag("messaging.system", "litebus");
            activity.SetTag("messaging.message.type", typeof(TMessage).FullName);
            
            // Store activity in the LiteBus Execution Context to retrieve it in later stages
            context.Items[ActivityKey] = activity;
        }
    }

    public void OnMediationCompleted<TMessage, TMessageResult>(TMessage message, TMessageResult result, IExecutionContext context) where TMessage : notnull
    {
        if (context.Items.TryGetValue(ActivityKey, out var activityObj) && activityObj is Activity activity)
        {
            activity.SetStatus(ActivityStatusCode.Ok);
            activity.Dispose();
        }
    }

    public void OnMediationFaulted<TMessage>(TMessage message, Exception exception, IExecutionContext context) where TMessage : notnull
    {
        if (context.Items.TryGetValue(ActivityKey, out var activityObj) && activityObj is Activity activity)
        {
            activity.SetStatus(ActivityStatusCode.Error, exception.Message);

            // Manually recording the exception as an Activity Event
            var tags = new ActivityTagsCollection
            {
                { "exception.type", exception.GetType().FullName },
                { "exception.message", exception.Message },
                { "exception.stacktrace", exception.StackTrace }
            };
            
            activity.AddEvent(new ActivityEvent("exception", DateTimeOffset.UtcNow, tags));
            activity.Dispose();
        }
    }
}