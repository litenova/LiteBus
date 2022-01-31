namespace LiteBus.Messaging.Abstractions;

public interface IInstance<out TInstance, out TDescriptor>
{
    public TInstance Instance { get; }

    public TDescriptor Descriptor { get; }
}