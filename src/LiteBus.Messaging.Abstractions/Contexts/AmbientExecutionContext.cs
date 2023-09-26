#nullable enable
using System.Threading;

namespace LiteBus.Messaging.Abstractions;

public static class AmbientExecutionContext
{
    private static readonly AsyncLocal<IExecutionContext?> ExecutionContextLocal = new();

    public static IExecutionContext? Current
    {
        get => ExecutionContextLocal.Value;
        set => ExecutionContextLocal.Value = value;
    }
}