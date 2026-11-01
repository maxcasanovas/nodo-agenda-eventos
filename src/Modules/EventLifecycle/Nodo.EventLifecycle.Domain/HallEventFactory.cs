using Nodo.SharedKernel;

namespace Nodo.EventLifecycle.Domain;

/// <summary>An extra service as frozen in the accepted quote.</summary>
public sealed record ExtraServiceSnapshot(
    ExtraServiceType Type, string Code, string Description, int Quantity, Money UnitCost);

/// <summary>Everything EventLifecycle needs from an accepted quote (Published Language, already mapped to domain types).</summary>
public sealed record AcceptedQuoteSnapshot(
    QuoteId QuoteId, CustomerId CustomerId, HallId HallId, TimeSlot Slot, Attendance Attendance,
    Money Total, IReadOnlyCollection<ExtraServiceSnapshot> ExtraServices);

/// <summary>Creates the event request when a quote is accepted (BR-11).</summary>
public static class HallEventFactory
{
    /// <param name="equipmentStock">Own-equipment stock by code (BR-08 / INV-13).</param>
    public static HallEvent FromAcceptedQuote(
        AcceptedQuoteSnapshot snapshot,
        DateTime nowUtc,
        DateOnly todayLocal,
        IReadOnlyDictionary<string, int> equipmentStock)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        BookingPolicy.EnsureWithinOperatingHours(snapshot.Slot);                    // BR-02
        BookingPolicy.EnsureWithinBookingWindow(snapshot.Slot.Date, todayLocal);    // BR-05
        var items = BuildExtraServices(snapshot.ExtraServices, equipmentStock);     // INV-13

        return HallEvent.CreateRequested(
            snapshot.QuoteId, snapshot.CustomerId, snapshot.HallId, snapshot.Slot, snapshot.Attendance,
            snapshot.Total, items, nowUtc);
    }

    private static List<ExtraServiceItem> BuildExtraServices(
        IReadOnlyCollection<ExtraServiceSnapshot> extras, IReadOnlyDictionary<string, int> equipmentStock)
    {
        var items = extras
            .Select(e => ExtraServiceItem.Create(e.Type, e.Code, e.Description, e.Quantity, e.UnitCost))
            .ToList();

        var duplicate = items.GroupBy(i => i.Code).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new DomainException(InvariantCodes.ExtraServiceInvalid,
                $"El servicio '{duplicate.Key}' está repetido; indica una sola línea con la cantidad total.");
        }

        foreach (var item in items.Where(i => i.Type == ExtraServiceType.OwnEquipment))
        {
            var stock = equipmentStock.TryGetValue(item.Code, out var s) ? s : 0;
            if (item.Quantity > stock)
            {
                throw new DomainException(InvariantCodes.ExtraServiceInvalid,
                    $"No hay suficiente '{item.Description}': se pidieron {item.Quantity} y el inventario es {stock}.");
            }
        }

        return items;
    }
}
