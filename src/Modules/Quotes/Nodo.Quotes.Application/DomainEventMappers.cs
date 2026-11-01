using Nodo.BuildingBlocks.Application;
using Nodo.Quotes.Contracts;
using Nodo.Quotes.Domain;

namespace Nodo.Quotes.Application;

// Domain event -> integration event. Explicit mapping: the integration event is the public contract.

internal sealed class QuoteIssuedMapper(IQuotesOutbox outbox) : DomainEventHandler<QuoteIssued>
{
    protected override Task HandleAsync(QuoteIssued e, CancellationToken cancellationToken)
    {
        outbox.Add(new QuoteIssuedIntegrationEvent(
            Guid.NewGuid(), e.OccurredOnUtc, e.QuoteId.Value, e.CustomerId.Value, e.Date, e.Start, e.End,
            e.Total.Amount, e.ExpiresAtUtc));
        return Task.CompletedTask;
    }
}

internal sealed class QuoteAcceptedMapper(IQuotesOutbox outbox) : DomainEventHandler<QuoteAccepted>
{
    protected override Task HandleAsync(QuoteAccepted e, CancellationToken cancellationToken)
    {
        var price = e.Price;
        outbox.Add(new QuoteAcceptedIntegrationEvent(
            Guid.NewGuid(), e.OccurredOnUtc, e.QuoteId.Value, e.CustomerId.Value, e.HallId.Value,
            e.Slot.Date, e.Slot.Start, e.Slot.End, e.Attendance.Value,
            price.BaseHourlyRate.Amount, price.Hours, price.RateFactor, price.HallRental.Amount,
            price.MembershipDiscountPercentage, price.MembershipDiscount.Amount, price.ExtrasTotal.Amount, price.Total.Amount,
            e.ExtraServices
                .Select(l => new QuoteExtraServiceDto(l.Type.ToString(), l.Code, l.Name, l.Quantity, l.UnitCost.Amount))
                .ToList()));
        return Task.CompletedTask;
    }
}

internal sealed class QuoteExpiredMapper(IQuotesOutbox outbox) : DomainEventHandler<QuoteExpired>
{
    protected override Task HandleAsync(QuoteExpired e, CancellationToken cancellationToken)
    {
        outbox.Add(new QuoteExpiredIntegrationEvent(Guid.NewGuid(), e.OccurredOnUtc, e.QuoteId.Value, e.CustomerId.Value));
        return Task.CompletedTask;
    }
}
