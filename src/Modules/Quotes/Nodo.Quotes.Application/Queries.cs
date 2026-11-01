using FluentValidation;
using Nodo.BuildingBlocks.Application;
using Nodo.Quotes.Domain;

namespace Nodo.Quotes.Application;

// ---- GetQuote ----

public sealed record GetQuoteQuery(Guid QuoteId) : IQuery<QuoteDto>;

internal sealed class GetQuoteValidator : AbstractValidator<GetQuoteQuery>
{
    public GetQuoteValidator() =>
        RuleFor(x => x.QuoteId).NotEmpty().WithMessage("La cotización es obligatoria.");
}

internal sealed class GetQuoteHandler(IQuoteQueries queries, ICustomerDirectory customers, IClock clock)
    : IQueryHandler<GetQuoteQuery, QuoteDto>
{
    public async Task<QuoteDto> Handle(GetQuoteQuery request, CancellationToken cancellationToken)
    {
        var quote = await queries.GetAsync(request.QuoteId, cancellationToken)
            ?? throw new NotFoundException($"No existe la cotización '{request.QuoteId}'.");

        return await QuoteEnricher.EnrichAsync(quote, customers, clock, cancellationToken);
    }
}

// ---- ListQuotes ----

public sealed record ListQuotesQuery(Guid? CustomerId = null, string? Status = null)
    : IQuery<IReadOnlyCollection<QuoteDto>>;

internal sealed class ListQuotesValidator : AbstractValidator<ListQuotesQuery>
{
    public ListQuotesValidator() =>
        RuleFor(x => x.Status)
            .Must(s => s is null || Enum.TryParse<QuoteStatus>(s, ignoreCase: true, out _))
            .WithMessage($"El estado debe ser uno de: {string.Join(", ", Enum.GetNames<QuoteStatus>())}.");
}

internal sealed class ListQuotesHandler(IQuoteQueries queries, ICustomerDirectory customers, IClock clock)
    : IQueryHandler<ListQuotesQuery, IReadOnlyCollection<QuoteDto>>
{
    public async Task<IReadOnlyCollection<QuoteDto>> Handle(ListQuotesQuery request, CancellationToken cancellationToken)
    {
        var quotes = await queries.ListAsync(request.CustomerId, request.Status, cancellationToken);

        var result = new List<QuoteDto>(quotes.Count);
        foreach (var quote in quotes)
            result.Add(await QuoteEnricher.EnrichAsync(quote, customers, clock, cancellationToken));
        return result;
    }
}

/// <summary>Adds what the database does not know: the customer's name and the hours left, from the clock.</summary>
internal static class QuoteEnricher
{
    public static async Task<QuoteDto> EnrichAsync(
        QuoteDto quote, ICustomerDirectory customers, IClock clock, CancellationToken cancellationToken)
    {
        var name = await customers.GetNameAsync(quote.CustomerId, cancellationToken);
        var remaining = quote.Status == nameof(QuoteStatus.Issued)
            ? Math.Max(0, (quote.ExpiresAtUtc - clock.UtcNow).TotalHours)
            : 0;

        return quote with { CustomerName = name, HoursRemaining = Math.Round(remaining, 2) };
    }
}
