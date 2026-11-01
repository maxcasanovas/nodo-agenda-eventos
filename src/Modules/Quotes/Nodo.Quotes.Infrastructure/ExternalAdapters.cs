using Nodo.External.Simulated.Memberships;
using Nodo.External.Simulated.Spaces;
using Nodo.Quotes.Application;

namespace Nodo.Quotes.Infrastructure;

// Anti-corruption at the module's border: the external contexts speak their own language, and these adapters are the
// only place in Quotes that knows it.

/// <summary>Customer/Supplier with Open Host Service: Spaces publishes a catalog, Quotes translates it to what it needs.</summary>
internal sealed class SpacesCatalogAdapter(SpacesContext spaces) : ISpacesCatalog
{
    public Task<HallInfo?> GetHallAsync(Guid hallId, CancellationToken cancellationToken = default)
    {
        var venue = spaces.FindVenue(hallId.ToString());
        return Task.FromResult(venue is null
            ? null
            : new HallInfo(Guid.Parse(venue.VenueUid), venue.DisplayName, venue.MaxCapacity));
    }

    public Task<IReadOnlyCollection<EquipmentItem>> GetEquipmentAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyCollection<EquipmentItem>>(spaces.Venues
            .SelectMany(v => v.Assets)
            .Select(a => new EquipmentItem(a.AssetCode, a.Label, a.UnitsInStock, a.FeePerEvent))
            .ToList());

    public Task<IReadOnlyCollection<CateringItem>> GetCateringMenuAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyCollection<CateringItem>>(spaces.CateringMenu
            .Select(i => new CateringItem(i.Sku, i.Title, i.PricePerHead))
            .ToList());
}

/// <summary>
/// Conformist: Quotes adopts what the Memberships context says, as it says it. <c>status == "ACTIVE"</c> is the only
/// reading it does, and the discount is passed on untouched (the domain still refuses anything outside 0-10 %, INV-17).
/// </summary>
internal sealed class MembershipDirectoryAdapter(MembershipsContext memberships) : IMembershipDirectory
{
    public Task<Membership> GetMembershipAsync(Guid customerId, CancellationToken cancellationToken = default)
    {
        var member = memberships.Find(customerId.ToString());
        return Task.FromResult(member is null
            ? new Membership(IsActiveMember: false, DiscountPercentage: 0)
            : new Membership(member.Status == "ACTIVE", member.DiscountPct));
    }
}

internal sealed class CustomerDirectoryAdapter(MembershipsContext memberships) : ICustomerDirectory
{
    public Task<string?> GetNameAsync(Guid customerId, CancellationToken cancellationToken = default) =>
        Task.FromResult(memberships.Find(customerId.ToString())?.FullName);
}
