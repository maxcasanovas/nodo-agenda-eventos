namespace Nodo.SharedKernel;

public interface IDomainEvent
{
    DateTime OccurredOnUtc { get; }
}
