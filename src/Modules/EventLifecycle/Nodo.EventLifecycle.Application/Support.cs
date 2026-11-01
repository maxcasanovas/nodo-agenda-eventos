using Nodo.BuildingBlocks.Application;
using Nodo.EventLifecycle.Domain;

namespace Nodo.EventLifecycle.Application;

internal static class RepositoryExtensions
{
    public static async Task<HallEvent> GetRequiredAsync(
        this IHallEventRepository events, Guid eventId, CancellationToken cancellationToken) =>
        await events.GetByIdAsync(new HallEventId(eventId), cancellationToken)
        ?? throw new NotFoundException($"No existe el evento '{eventId}'.");
}

/// <summary>
/// Gives commands the customer's credit, opening it on first use. Keeps one instance per customer for the whole
/// command, so several grants in one transaction (e.g. the expiry job) never create duplicate rows.
/// </summary>
internal sealed class CustomerCreditAccessor(ICustomerCreditRepository credits)
{
    private readonly Dictionary<CustomerId, CustomerCredit> _cache = [];

    /// <returns>The existing credit, or null if the customer never had any.</returns>
    public async Task<CustomerCredit?> FindAsync(CustomerId customerId, CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(customerId, out var cached))
            return cached;

        var credit = await credits.GetByCustomerAsync(customerId, cancellationToken);
        if (credit is not null)
            _cache[customerId] = credit;
        return credit;
    }

    public async Task<CustomerCredit> GetOrOpenAsync(CustomerId customerId, CancellationToken cancellationToken)
    {
        var credit = await FindAsync(customerId, cancellationToken);
        if (credit is not null)
            return credit;

        credit = CustomerCredit.Open(customerId);
        await credits.AddAsync(credit, cancellationToken);
        _cache[customerId] = credit;
        return credit;
    }
}
