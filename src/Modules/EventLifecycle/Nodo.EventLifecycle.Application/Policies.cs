using Nodo.BuildingBlocks.Application;
using Nodo.EventLifecycle.Domain;
using Nodo.HallSchedule.Contracts;
using Nodo.Quotes.Contracts;
using Nodo.SharedKernel;

namespace Nodo.EventLifecycle.Application;

/// <summary>
/// Policy: when Quotes publishes an accepted quote, create the event request and provisionally block the hall (BR-11).
/// Idempotency comes from the inbox decorator that wraps every integration handler. If the hall is no longer free,
/// the request is created and then rejected by the system.
/// </summary>
internal sealed class QuoteAcceptedHandler(
    ISpacesCatalog spaces,
    IHallEventRepository events,
    IHallScheduleModuleApi hallSchedule,
    IEventLifecycleUnitOfWork unitOfWork,
    IClock clock) : IIntegrationEventHandler<QuoteAcceptedIntegrationEvent>
{
    public const string SystemActor = "Sistema";
    public const string NoAvailabilityReason = "Sin disponibilidad al registrar la solicitud";

    public async Task HandleAsync(QuoteAcceptedIntegrationEvent message, CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var stock = await spaces.GetEquipmentStockAsync(cancellationToken);
        var snapshot = ToSnapshot(message);

        var hallEvent = HallEventFactory.FromAcceptedQuote(snapshot, now, clock.TodayLocal, stock);

        // Commit the request first (the inbox decorator's registration is committed with it): a later failure leaves a recoverable request,
        // never a block without an event.
        await events.AddAsync(hallEvent, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var placement = await hallSchedule.PlaceProvisionalBlockAsync(
            hallEvent.Id.Value, message.HallId, message.Date, message.Start, message.End,
            HallEventMapping.OwnEquipmentOf(hallEvent), cancellationToken);

        if (!placement.Success)
        {
            hallEvent.Reject(SystemActor, NoAvailabilityReason, now);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }

    private static AcceptedQuoteSnapshot ToSnapshot(QuoteAcceptedIntegrationEvent m) => new(
        new QuoteId(m.QuoteId),
        new CustomerId(m.CustomerId),
        new HallId(m.HallId),
        TimeSlot.Create(m.Date, m.Start, m.End),
        Attendance.Create(m.Attendance),
        Money.Of(m.Total),
        m.ExtraServices
            .Select(x => new ExtraServiceSnapshot(
                Enum.Parse<ExtraServiceType>(x.Type), x.Code, x.Name, x.Quantity, Money.Of(x.UnitCost)))
            .ToList());
}

/// <summary>Policy (BR-20): once the event is confirmed in firm, its extra services are assigned.</summary>
internal sealed class AssignExtraServicesOnConfirmed(IHallEventRepository events)
    : DomainEventHandler<HallEventFirmlyConfirmed>
{
    protected override async Task HandleAsync(HallEventFirmlyConfirmed domainEvent, CancellationToken cancellationToken)
    {
        var hallEvent = await events.GetRequiredAsync(domainEvent.EventId.Value, cancellationToken);

        // Same transaction as the confirmation: the unit of work keeps dispatching the event this raises.
        hallEvent.AssignExtraServices(domainEvent.OccurredOnUtc);
    }
}
