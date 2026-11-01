using Microsoft.EntityFrameworkCore;
using Nodo.Quotes.Application;
using Nodo.Quotes.Domain;

namespace Nodo.Quotes.Infrastructure.Persistence;

/// <summary>Read side: projections with AsNoTracking. The handler adds the customer name and the hours remaining.</summary>
internal sealed class QuoteQueries(QuotesDbContext db) : IQuoteQueries
{
    public async Task<QuoteDto?> GetAsync(Guid quoteId, CancellationToken cancellationToken = default)
    {
        var id = new QuoteId(quoteId);
        var row = await Rows(db.Quotes.AsNoTracking().Where(q => q.Id == id)).FirstOrDefaultAsync(cancellationToken);
        return row is null ? null : ToDto(row);
    }

    public async Task<IReadOnlyCollection<QuoteDto>> ListAsync(
        Guid? customerId, string? status, CancellationToken cancellationToken = default)
    {
        var quotes = db.Quotes.AsNoTracking();

        if (customerId is { } customer)
        {
            var id = new CustomerId(customer);
            quotes = quotes.Where(q => q.CustomerId == id);
        }

        if (status is not null)
        {
            var parsed = Enum.Parse<QuoteStatus>(status, ignoreCase: true);
            quotes = quotes.Where(q => q.Status == parsed);
        }

        var rows = await Rows(quotes.OrderByDescending(q => q.IssuedAtUtc)).ToListAsync(cancellationToken);
        return rows.Select(ToDto).ToList();
    }

    private static IQueryable<QuoteRow> Rows(IQueryable<Quote> quotes) => quotes.Select(q => new QuoteRow(
        q.Id, q.CustomerId, q.HallId, q.Slot.Date, q.Slot.Start, q.Slot.End, q.Attendance.Value, q.Status,
        q.IssuedAtUtc, q.ExpiresAtUtc, q.AcceptedAtUtc, q.Price,
        q.ExtraServices.Select(e => new ExtraRow(e.Type, e.Code, e.Name, e.Quantity, e.UnitCost.Amount)).ToList()));

    private static QuoteDto ToDto(QuoteRow r) => new(
        r.Id.Value, r.CustomerId.Value, null, r.HallId.Value, r.Date, r.Start, r.End, r.Attendance, r.Status.ToString(),
        r.IssuedAtUtc, r.ExpiresAtUtc, r.AcceptedAtUtc, 0,
        new PriceBreakdownDto(
            r.Price.BaseHourlyRate.Amount, r.Price.Hours, r.Price.RateFactor, r.Price.HallRental.Amount,
            r.Price.MembershipDiscountPercentage, r.Price.MembershipDiscount.Amount, r.Price.ExtrasTotal.Amount,
            r.Price.Total.Amount),
        r.Extras.Select(e => new ExtraServiceLineDto(
            e.Type.ToString(), e.Code, e.Name, e.Quantity, e.UnitCost, e.UnitCost * e.Quantity)).ToList());

    private sealed record QuoteRow(
        QuoteId Id, CustomerId CustomerId, HallId HallId, DateOnly Date, TimeOnly Start, TimeOnly End, int Attendance,
        QuoteStatus Status, DateTime IssuedAtUtc, DateTime ExpiresAtUtc, DateTime? AcceptedAtUtc, PriceBreakdown Price,
        List<ExtraRow> Extras);

    private sealed record ExtraRow(ExtraServiceType Type, string Code, string Name, int Quantity, decimal UnitCost);
}
