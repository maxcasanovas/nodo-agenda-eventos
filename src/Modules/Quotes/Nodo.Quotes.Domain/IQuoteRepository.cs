namespace Nodo.Quotes.Domain;

public interface IQuoteRepository
{
    Task<Quote?> GetByIdAsync(QuoteId id, CancellationToken cancellationToken = default);

    Task AddAsync(Quote quote, CancellationToken cancellationToken = default);

    /// <summary>Issued quotes whose validity ended at or before <paramref name="nowUtc"/> (for the expiry job, BR-09).</summary>
    Task<IReadOnlyCollection<Quote>> ListDueForExpirationAsync(DateTime nowUtc, CancellationToken cancellationToken = default);
}
