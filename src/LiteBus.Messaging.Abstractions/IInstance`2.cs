namespace LiteBus.Messaging.Abstractions;

public interface IInstance<out TDescriptor>
{
    public IHandler Instance { get; }

    public TDescriptor Descriptor { get; }
}