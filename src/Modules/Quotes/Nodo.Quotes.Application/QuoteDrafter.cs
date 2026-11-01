using Nodo.BuildingBlocks.Application;
using Nodo.HallSchedule.Contracts;
using Nodo.Quotes.Domain;
using Nodo.SharedKernel;

namespace Nodo.Quotes.Application;

/// <summary>
/// Turns a request into a domain <see cref="Quote"/>: resolves catalog prices and the membership discount,
/// then lets <see cref="Quote.Issue"/> validate and price it. Shared by preview and issue.
/// </summary>
internal sealed class QuoteDrafter(
    ISpacesCatalog spaces, IMembershipDirectory memberships, IHallScheduleModuleApi hallSchedule, IClock clock)
{
    public async Task<Quote> BuildAsync(
        Guid customerId, Guid hallId, DateOnly date, TimeOnly start, TimeOnly end, int attendance,
        IReadOnlyCollection<ExtraServiceRequest> extras, CancellationToken cancellationToken)
    {
        _ = await spaces.GetHallAsync(hallId, cancellationToken)
            ?? throw new NotFoundException($"No existe el salón '{hallId}'.");

        var equipment = await spaces.GetEquipmentAsync(cancellationToken);
        var catering = await spaces.GetCateringMenuAsync(cancellationToken);
        var lines = extras.Select(e => ToLine(e, equipment, catering, attendance)).ToList();
        var stock = equipment.ToDictionary(e => e.Code, e => e.Stock, StringComparer.OrdinalIgnoreCase);

        var membership = await memberships.GetMembershipAsync(customerId, cancellationToken);
        var discount = MembershipDiscountPolicy.Effective(membership.IsActiveMember, membership.DiscountPercentage);

        return Quote.Issue(
            new CustomerId(customerId), new HallId(hallId), TimeSlot.Create(date, start, end),
            Attendance.Create(attendance), lines, stock, discount, clock.UtcNow, clock.TodayLocal);
    }

    /// <summary>Informative availability of the quote's slot and equipment (BR-10).</summary>
    public async Task<AvailabilityInfoDto> CheckAvailabilityAsync(Quote quote, CancellationToken cancellationToken)
    {
        var equipment = quote.ExtraServices
            .Where(e => e.Type == ExtraServiceType.OwnEquipment)
            .Select(e => new EquipmentRequest(e.Code, e.Quantity))
            .ToList();

        var result = await hallSchedule.CheckAvailabilityAsync(
            quote.HallId.Value, quote.Slot.Date, quote.Slot.Start, quote.Slot.End, equipment, null, cancellationToken);

        return new AvailabilityInfoDto(result.Success, result.Code, result.Reason);
    }

    private static ExtraServiceLine ToLine(
        ExtraServiceRequest request, IReadOnlyCollection<EquipmentItem> equipment,
        IReadOnlyCollection<CateringItem> catering, int attendance)
    {
        var item = equipment.FirstOrDefault(e => string.Equals(e.Code, request.Code, StringComparison.OrdinalIgnoreCase));
        if (item is not null)
        {
            return ExtraServiceLine.Create(
                item.Code, item.Name, ExtraServiceType.OwnEquipment, request.Quantity ?? 1, Money.Of(item.CostPerEvent));
        }

        var meal = catering.FirstOrDefault(c => string.Equals(c.Code, request.Code, StringComparison.OrdinalIgnoreCase));
        if (meal is not null)
        {
            return ExtraServiceLine.Create(
                meal.Code, meal.Name, ExtraServiceType.ExternalCatering, request.Quantity ?? attendance,
                Money.Of(meal.CostPerPerson));
        }

        throw new NotFoundException($"No existe el servicio extra '{request.Code}'.");
    }
}
