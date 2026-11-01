namespace Nodo.EventLifecycle.Domain;

public interface IHallEventRepository
{
    Task<HallEvent?> GetByIdAsync(HallEventId id, CancellationToken cancellationToken = default);

    Task AddAsync(HallEvent hallEvent, CancellationToken cancellationToken = default);

    /// <summary>True if the hall already has a non-final event (Requested, Approved, Confirmed or InProgress) overlapping the slot.</summary>
    Task<bool> ExistsActiveForSlotAsync(HallId hallId, TimeSlot slot, CancellationToken cancellationToken = default);

    /// <summary>
    /// Requested events past their decision deadline and ApprovedByManagement events past their deposit deadline
    /// (for the expiry job, BR-12 / BR-17).
    /// </summary>
    Task<IReadOnlyCollection<HallEvent>> ListOverdueAsync(DateTime nowUtc, CancellationToken cancellationToken = default);
}

public interface ICustomerCreditRepository
{
    Task<CustomerCredit?> GetByCustomerAsync(CustomerId customerId, CancellationToken cancellationToken = default);

    Task AddAsync(CustomerCredit credit, CancellationToken cancellationToken = default);
}
