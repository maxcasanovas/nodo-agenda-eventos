using Nodo.External.Simulated.Spaces;
using Nodo.HallSchedule.Application;

namespace Nodo.HallSchedule.Infrastructure;

/// <summary>Customer/Supplier with Open Host Service: HallSchedule only needs the stock of each own-equipment item.</summary>
internal sealed class SpacesCatalogAdapter(SpacesContext spaces) : ISpacesCatalog
{
    public Task<IReadOnlyDictionary<string, int>> GetEquipmentStockAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<string, int>>(spaces.Venues
            .SelectMany(v => v.Assets)
            .ToDictionary(a => a.AssetCode, a => a.UnitsInStock, StringComparer.OrdinalIgnoreCase));
}
