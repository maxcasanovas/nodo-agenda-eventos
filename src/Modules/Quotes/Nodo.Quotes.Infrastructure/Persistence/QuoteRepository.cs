using Microsoft.EntityFrameworkCore;
using Nodo.Quotes.Domain;

namespace Nodo.Quotes.Infrastructure.Persistence;

internal sealed class QuoteRepository(QuotesDbContext db) : IQuoteRepository
{
    public async Task<Quote?> GetByIdAsync(QuoteId id, CancellationToken cancellationToken = default) =>
        await db.Quotes.FirstOrDefaultAsync(q => q.Id == id, cancellationToken);

    public async Task AddAsync(Quote quote, CancellationToken cancellationToken = default) =>
        await db.Quotes.AddAsync(quote, cancellationToken);

    public async Task<IReadOnlyCollection<Quote>> ListDueForExpirationAsync(
        DateTime nowUtc, CancellationToken cancellationToken = default) =>
        await db.Quotes
            .Where(q => q.Status == QuoteStatus.Issued && q.ExpiresAtUtc <= nowUtc)
            .ToListAsync(cancellationToken);
}
